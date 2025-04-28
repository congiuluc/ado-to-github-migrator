using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Models.Ado;
using AzureDevOps2GitHubMigrator.Models.GitHub;
using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class ExportGithubUsersCommand
{
    public static Command Create()
    {
        var command = new Command("gh-export-users", "Export GitHub organization users to CSV file");

        // Required options but can be provided via config
        var githubOrgOption = new Option<string>("--gh-org", "GitHub organization name");
        var githubPatOption = new Option<string>("--gh-pat", "GitHub Personal Access Token");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");

        command.AddOption(githubOrgOption);
        command.AddOption(githubPatOption);
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
            var finalGithubOrg = context.ParseResult.GetValueForOption(githubOrgOption) ?? config["GitHub:Organization"];
            var finalGithubPat = context.ParseResult.GetValueForOption(githubPatOption) ?? config["GitHub:Pat"];

            var missingParameters = new List<string>();
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

            using var httpClient = new HttpClient();
            var githubService = new GitHubService(httpClient, finalGithubPat ?? "");

            try
            {
                Logger.LogInfo($"Retrieving users from GitHub organization: {finalGithubOrg}");
                var users = await githubService.GetOrgUsersAsync(finalGithubOrg!, string.Empty);
                var samlUsers = await githubService.GetSamlIdentitiesAsync(finalGithubOrg!, string.Empty);
                var hasSaml = samlUsers.Any();

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                var outputFile = $"{timestamp}_{finalGithubOrg}_github_users.csv";

                await File.WriteAllLinesAsync(outputFile, GenerateGitHubUsersCsvContent(users, samlUsers, hasSaml));
                Logger.LogSuccess($"\nSuccessfully exported {users.Count()} users to: {outputFile}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"User extraction failed: {ex.Message}", ex);
            }
        });

        return command;
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