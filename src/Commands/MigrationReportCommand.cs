using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.Models;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class MigrationReportCommand
{
    public static Command Create()
    {
        var command = new Command("migration-report", "Generate migration report");

        // Required options for both ADO and GitHub, but can be provided via config
        var adoOrgOption = new Option<string>("--ado-org", "Azure DevOps organization name");
        var adoPatOption = new Option<string>("--ado-pat", "Azure DevOps Personal Access Token");
        var githubOrgOption = new Option<string>("--gh-org", "GitHub organization name");
        var githubPatOption = new Option<string>("--gh-pat", "GitHub Personal Access Token");

        // Optional ADO options
        var adoVersionOption = new Option<string>("--ado-version",  "Azure DevOps version (e.g., 2019, 2020, 2022, cloud)");
        var adoBaseUrlOption = new Option<string>("--ado-baseurl", "Azure DevOps Server base URL");
        var adoProjectsOption = new Option<string>("--ado-projects", "Comma-separated list of project names");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var usersMappingFileOption = new Option<string>("--users-mapping-file", "Path to the users mapping file");
        var repoPatternOption = new Option<string>("--repo-name-pattern", "Pattern for GitHub repository names. Available placeholders: {orgName}, {projectName}, {repoName}");
        var teamPatternOption = new Option<string>("--team-name-pattern", "Pattern for GitHub team names. Available placeholders: {orgName}, {projectName}, {teamName}");
             // Add an optional configuration file path option
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");

        command.AddOption(adoOrgOption);
        command.AddOption(adoPatOption);
        command.AddOption(adoVersionOption);
        command.AddOption(adoBaseUrlOption);
        command.AddOption(adoProjectsOption);
        command.AddOption(githubOrgOption);
        command.AddOption(githubPatOption);
        command.AddOption(workingDirOption);
        command.AddOption(configFilePathOption);
        command.AddOption(usersMappingFileOption);
        command.AddOption(repoPatternOption);
        command.AddOption(teamPatternOption);

        // Update the handler to use InvocationContext
        command.SetHandler(async (InvocationContext context) =>
        {
            // Load configuration first since we need it for working directory
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
            var finalGithubOrg = context.ParseResult.GetValueForOption(githubOrgOption) ?? config["GitHub:Organization"];
            var finalGithubPat = context.ParseResult.GetValueForOption(githubPatOption) ?? config["GitHub:Pat"];
            var adoVersion = context.ParseResult.GetValueForOption(adoVersionOption) ?? config["AzureDevOps:Version"] ?? "cloud";
            var adoBaseUrl = context.ParseResult.GetValueForOption(adoBaseUrlOption) ?? config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com";
            var adoProjects = context.ParseResult.GetValueForOption(adoProjectsOption) ?? config["AzureDevOps:Projects"];
            var usersMappingFile = context.ParseResult.GetValueForOption(usersMappingFileOption) ?? config["Migration:UsersMappingFile"];
            var repoPattern = context.ParseResult.GetValueForOption(repoPatternOption) ?? config["Migration:RepoNamePattern"] ?? "{repoName}";
            var teamPattern = context.ParseResult.GetValueForOption(teamPatternOption) ?? config["Migration:TeamNamePattern"] ?? "{teamName}";
     
            // Validate required values
            var missingParameters = new List<string>();
            
            if (string.IsNullOrEmpty(finalAdoOrg))
                missingParameters.Add("Azure DevOps organization (--ado-org)");
            if (string.IsNullOrEmpty(finalAdoPat))
                missingParameters.Add("Azure DevOps PAT (--ado-pat)");
            if (string.IsNullOrEmpty(finalGithubOrg))
            if (string.IsNullOrEmpty(finalGithubOrg))
                missingParameters.Add("GitHub organization (--gh-org)");
            if (string.IsNullOrEmpty(finalGithubPat))
                missingParameters.Add("GitHub PAT (--gh-pat)");
            if (missingParameters.Any())
            {
                Logger.LogError($"The following required parameters are missing: {string.Join(", ", missingParameters)}");
                Logger.LogError("Please provide them via command-line options or in the configuration file.");
                return; // Gracefully exit
            }

            var adoUrl = $"{adoBaseUrl}/{finalAdoOrg}";

            using var httpClient = new HttpClient();
            var adoService = new AzureDevOpsService(httpClient, adoUrl, finalAdoPat!, adoVersion);
            using var httpClientGH = new HttpClient();
            var githubService = new GitHubService(httpClientGH, finalGithubPat!);

            try
            {
                Logger.LogInfo("Running assessment...");
                var migrationService = new MigrationService(adoService, githubService);
                var migrationProjects = await migrationService.RunAssessmentAsync(finalAdoOrg,adoProjects, finalGithubOrg!, repoPattern, teamPattern, usersMappingFile!);

                var reportFile = $"{DateTime.Now:yyyyMMddHHmm}_{finalAdoOrg}_migration_report.md";
                MigrationReport.GenerateMarkdownReport(migrationProjects, finalAdoOrg!, adoUrl, finalGithubOrg!, reportFile);
                Logger.LogSuccess($"Assessment report generated successfully. Report saved to {workingDir}\\{reportFile}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Report generation failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}