using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Models.Ado;
using AzureDevOps2GitHubMigrator.Models.GitHub;
using AzureDevOps2GitHubMigrator.Utils;
using Microsoft.Extensions.Configuration;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;

namespace AzureDevOps2GitHubMigrator.Commands;

public class UserMappingCommand
{
    public static Command Create()
    {
        var command = new Command("user-mapping", "Create a mapping file between Azure DevOps and GitHub users");

        // Required options but can be provided via config
        var adoOrgOption = new Option<string>("--ado-org", "Azure DevOps organization name");
        var adoPatOption = new Option<string>("--ado-pat", "Azure DevOps Personal Access Token");
        var githubOrgOption = new Option<string>("--gh-org", "GitHub organization name");
        var githubPatOption = new Option<string>("--gh-pat", "GitHub Personal Access Token");

        // Optional options with defaults
        var adoVersionOption = new Option<string>("--ado-version", "Azure DevOps version (e.g., 2019, 2020)");
        var adoBaseUrlOption = new Option<string>("--ado-baseurl", "Azure DevOps Server base URL");
        var adoProjectsOption = new Option<string>("--ado-projects", "Comma-separated list of project names");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");

        command.AddOption(adoOrgOption);
        command.AddOption(adoPatOption);
        command.AddOption(githubOrgOption);
        command.AddOption(githubPatOption);
        command.AddOption(adoVersionOption);
        command.AddOption(adoBaseUrlOption);
        command.AddOption(adoProjectsOption);
        command.AddOption(workingDirOption);
        command.AddOption(configFilePathOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            // Load configuration first
            var configBuilder = new ConfigurationBuilder();
            var configFilePath = context.ParseResult.GetValueForOption(configFilePathOption);
            if (!string.IsNullOrEmpty(configFilePath))
            {
                configBuilder.AddJsonFile(configFilePath, optional: true);
            }
            else
            {
                var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "migrator_config.json");
                configBuilder.AddJsonFile(defaultConfigPath, optional: true);
            }
            var config = configBuilder.Build();

            // Get working directory from command line or fall back to config, then default
            var workingDir = context.ParseResult.GetValueForOption(workingDirOption) ?? 
                            config["WorkingDirectory"] ?? 
                            Path.Combine(AppContext.BaseDirectory, "temp");
            
            Directory.CreateDirectory(workingDir);
            Directory.SetCurrentDirectory(workingDir);

            // Get values from command line or fall back to config
            var finalAdoOrg = context.ParseResult.GetValueForOption(adoOrgOption) ?? config["AzureDevOps:Organization"];
            var finalAdoPat = context.ParseResult.GetValueForOption(adoPatOption) ?? config["AzureDevOps:Pat"];
            var finalAdoVersion = context.ParseResult.GetValueForOption(adoVersionOption) ?? config["AzureDevOps:Version"] ?? "cloud";
            var finalAdoBaseUrl = context.ParseResult.GetValueForOption(adoBaseUrlOption) ?? config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com";
            var finalGithubOrg = context.ParseResult.GetValueForOption(githubOrgOption) ?? config["GitHub:Organization"];
            var finalGithubPat = context.ParseResult.GetValueForOption(githubPatOption) ?? config["GitHub:Pat"];
            var adoProjects = context.ParseResult.GetValueForOption(adoProjectsOption) ?? config["AzureDevOps:Projects"];

            var missingParameters = new List<string>();
            if (string.IsNullOrEmpty(finalAdoOrg))
                missingParameters.Add("Azure DevOps organization (--ado-org)");
            if (string.IsNullOrEmpty(finalAdoPat))
                missingParameters.Add("Azure DevOps PAT (--ado-pat)");
            if (string.IsNullOrEmpty(finalGithubOrg))
                missingParameters.Add("GitHub organization (--gh-org)");
            if (string.IsNullOrEmpty(finalGithubPat))
                missingParameters.Add("GitHub PAT (--gh-pat)");

            if (missingParameters.Any())
            {
                Logger.LogError($"The following required parameters are missing: {string.Join(", ", missingParameters)}");
                Logger.LogError("Please provide them via command-line options or in the configuration file.");
                return;
            }

            try
            {
                var adoOrgBaseUrl= $"{finalAdoBaseUrl}/{finalAdoOrg}";
                using var adoHttpClient = new HttpClient();
                var adoService = new AzureDevOpsService(adoHttpClient, adoOrgBaseUrl, finalAdoPat!, finalAdoVersion);
                
                using var githubHttpClient = new HttpClient();
                var githubService = new GitHubService(githubHttpClient, finalGithubPat!);

                Logger.LogInfo($"Retrieving users from Azure DevOps organization: {finalAdoOrg}");
                
                
                var adoUsers = await adoService.ExtractUsersAsync(adoProjects);

                Logger.LogInfo($"Retrieving users from GitHub organization: {finalGithubOrg}");
                var githubUsers = await githubService.GetOrgUsersAsync(finalGithubOrg!, string.Empty);
                var samlUsers = await githubService.GetSamlIdentitiesAsync(finalGithubOrg!, string.Empty);
                var hasSaml = samlUsers.Any();

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                var outputFile = $"{timestamp}_user_mapping.csv";

                await File.WriteAllLinesAsync(outputFile, GenerateUserMappingCsvContent(adoUsers, githubUsers, samlUsers, hasSaml));
                Logger.LogSuccess($"\nSuccessfully created user mapping file: {outputFile}");
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError($"Failed to connect to service: {ex.Message}", ex);
                Logger.LogError("Please verify your PAT and organization details are correct.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"User mapping creation failed: {ex.Message}", ex);
            }
        });

        return command;
    }

    private static IEnumerable<string> GenerateUserMappingCsvContent(
        IEnumerable<AdoTeamMember> adoUsers, 
        IEnumerable<GitHubUser> githubUsers,
        IEnumerable<SAMLUserIdentity> samlUsers,
        bool hasSaml)
    {
        // Header
        if (hasSaml)
        {
            yield return "ADO_UPN,ADO_Username,GitHub_Username,GitHub_Email,SAML_Identity";
        }
        else
        {
            yield return "ADO_UPN,ADO_Username,GitHub_Username,GitHub_Email";
        }

        // Generate rows for each ADO user
        foreach (var adoUser in adoUsers.Where(u => u.Identity != null))
        {
            var upn = adoUser.Identity?.UniqueName ?? "";
            var adoUsername = adoUser.Identity?.DisplayName ?? "";
            
            // Try to find a matching GitHub user by email or SAML identity
            var githubUser = githubUsers.FirstOrDefault(g => 
                (g.Email != null && upn.Equals(g.Email, StringComparison.OrdinalIgnoreCase)) ||
                samlUsers.Any(s => s.Login == g.Login && s.SamlIdentity.Equals(upn, StringComparison.OrdinalIgnoreCase)));

            var githubUsername = githubUser?.Login ?? "";
            var githubEmail = githubUser?.Email ?? "";
            var samlIdentity = "";

            if (hasSaml && githubUser != null)
            {
                samlIdentity = samlUsers.FirstOrDefault(s => s.Login == githubUser.Login)?.SamlIdentity ?? "";
            }

            if (hasSaml)
            {
                yield return $"{EscapeCsvField(upn)},{EscapeCsvField(adoUsername)},{EscapeCsvField(githubUsername)},{EscapeCsvField(githubEmail)},{EscapeCsvField(samlIdentity)}";
            }
            else
            {
                yield return $"{EscapeCsvField(upn)},{EscapeCsvField(adoUsername)},{EscapeCsvField(githubUsername)},{EscapeCsvField(githubEmail)}";
            }
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}