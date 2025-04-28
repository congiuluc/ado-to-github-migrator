using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Models.Ado;
using AzureDevOps2GitHubMigrator.Models.GitHub;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.GitHub;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class ExportCommand
{
    public static Command CreateExportUsersCommand()
    {
        var command = new Command("ado-export-users", "Extract Azure DevOps users to CSV file");

        // Required options but can be provided via config
        var adoOrgOption = new Option<string>("--ado-org", "Azure DevOps organization name");
        var adoPatOption = new Option<string>("--ado-pat", "Azure DevOps Personal Access Token");

        // Optional options with defaults
        var adoVersionOption = new Option<string>("--ado-version", () => "cloud", "Azure DevOps version (e.g., 2019, 2020)");
        var adoBaseUrlOption = new Option<string>("--ado-baseurl", () => "https://dev.azure.com", "Azure DevOps Server base URL");
        var adoProjectsOption = new Option<string>("--ado-projects", "Comma-separated list of project names");
        var includeInactiveOption = new Option<bool>("--include-inactive", () => false, "Include inactive users in the export");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");

        command.AddOption(adoOrgOption);
        command.AddOption(adoPatOption);
        command.AddOption(adoVersionOption);
        command.AddOption(adoBaseUrlOption);
        command.AddOption(adoProjectsOption);
        command.AddOption(includeInactiveOption);
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
            var adoVersion = context.ParseResult.GetValueForOption(adoVersionOption) ?? config["AzureDevOps:Version"] ?? "cloud";
            var adoBaseUrl = context.ParseResult.GetValueForOption(adoBaseUrlOption) ?? config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com";
            var adoProjects = context.ParseResult.GetValueForOption(adoProjectsOption) ?? config["AzureDevOps:Projects"];
            var includeInactive = context.ParseResult.GetValueForOption(includeInactiveOption);

            // Validate required values
            if (string.IsNullOrEmpty(finalAdoOrg))
                throw new ArgumentException("Azure DevOps organization is required. Provide it via --ado-org or in config file.");
            if (string.IsNullOrEmpty(finalAdoPat))
                throw new ArgumentException("Azure DevOps PAT is required. Provide it via --ado-pat or in config file.");

            var adoUrl = $"{adoBaseUrl}/{finalAdoOrg}";
            using var httpClient = new HttpClient();
            var adoService = new AzureDevOpsService(httpClient, adoUrl, finalAdoPat, adoVersion);

            try
            {
                Logger.LogInfo($"Retrieving users from Azure DevOps organization: {finalAdoOrg}");
                var users = await adoService.GetTeamMembersAsync(adoProjects, includeInactive);

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                var outputFile = $"{timestamp}_{finalAdoOrg}_users.csv";

                await File.WriteAllLinesAsync(outputFile, GenerateUsersCsvContent(users));
                Logger.LogSuccess($"\nSuccessfully exported {users.Count()} users to: {outputFile}");
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError($"Failed to connect to Azure DevOps: {ex.Message}", ex);
                Logger.LogError("Please verify your PAT and organization details are correct.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"User extraction failed: {ex.Message}", ex);
            }
        });

        return command;
    }

    public static Command CreateExportGithubUsersCommand()
    {
        var command = new Command("gh-export-users", "Export GitHub organization users to CSV file");

        // Required options but can be provided via config
        var githubOrgOption = new Option<string>("--gh-org", "GitHub organization name");
        var githubPatOption = new Option<string>("--gh-pat", "GitHub Personal Access Token");

        command.AddOption(githubOrgOption);
        command.AddOption(githubPatOption);

        command.SetHandler(async (string? org, string? pat) =>
        {
            // Load configuration
            var config = new ConfigurationBuilder()
                .AddJsonFile("migrator_config.json", optional: true)
                .Build();

            // Get values from command line or fall back to config
            var finalOrg = org ?? config["GitHub:Organization"];
            var finalPat = pat ?? config["GitHub:Pat"];

            // Validate required values
            if (string.IsNullOrEmpty(finalOrg))
                throw new ArgumentException("GitHub organization is required. Provide it via --gh-org or in config file.");
            if (string.IsNullOrEmpty(finalPat))
                throw new ArgumentException("GitHub PAT is required. Provide it via --gh-pat or in config file.");

            using var httpClient = new HttpClient();
            var githubService = new GitHubService(httpClient, token: finalPat);

            try
            {
                Logger.LogInfo($"Exporting users from GitHub organization: {finalOrg}");

                var hasSaml = await githubService.HasSamlEnabledAsync(finalOrg);
                var users = await githubService.GetOrgUsersAsync(finalOrg, string.Empty);
                var samlUsers = new List<SAMLUserIdentity>();

                if (hasSaml)
                {
                    Logger.LogInfo("SAML is enabled, fetching SAML identities...");
                    samlUsers = (await githubService.GetSamlIdentitiesAsync(finalOrg, string.Empty)).ToList();
                }

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                var outputFile = $"{timestamp}_{finalOrg}_github_users.csv";

                await File.WriteAllLinesAsync(outputFile, GenerateGitHubUsersCsvContent(users, samlUsers, hasSaml));
                Logger.LogSuccess($"\nSuccessfully exported {users.Count()} users to: {outputFile}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"User extraction failed: {ex.Message}", ex);
            }
        },
        githubOrgOption, githubPatOption);

        return command;
    }

    private static IEnumerable<string> GenerateUsersCsvContent(IEnumerable<AdoTeamMember> users)
    {
        yield return $"DisplayName,UPN,GitHub,IsActive";
        foreach (var user in users)
        {
            var isActive = user.Identity?.IsEnabled ?? true;
            var githubLogin = user.Identity?.Properties?.GetValueOrDefault("GitHubLogin", "") ?? "";
            yield return $"{EscapeCsvField(user.Identity?.DisplayName ?? "")},{EscapeCsvField(user.Identity?.UniqueName ?? "")},{EscapeCsvField(githubLogin)},{isActive}";
        }
    }

    private static IEnumerable<string> GenerateGitHubUsersCsvContent(IEnumerable<GitHubUser> users, IEnumerable<SAMLUserIdentity> samlUsers, bool hasSaml)
    {
        if (hasSaml)
        {
            yield return "GitHubLogin,GitHubName,GitHubEmail,SAMLIdentity";
            foreach (var user in users)
            {
                var samlIdentity = samlUsers.FirstOrDefault(s => s.Login == user.Login)?.SamlIdentity ?? "";
                yield return $"{EscapeCsvField(user.Login ?? "")},{EscapeCsvField(user.Name ?? "")},{EscapeCsvField(user.Email ?? "")},{EscapeCsvField(samlIdentity)}";
            }
        }
        else
        {
            yield return "GitHubLogin,GitHubName,GitHubEmail";
            foreach (var user in users)
            {
                yield return $"{EscapeCsvField(user.Login ?? "")},{EscapeCsvField(user.Name ?? "")},{EscapeCsvField(user.Email ?? "")}";
            }
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}