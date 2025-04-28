using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class MigrationCommand
{
    public static Command Create()
    {
        var command = new Command("migrate", "Migrate repositories from Azure DevOps to GitHub");

        // Required options but can be provided via config
        var adoOrgOption = new Option<string>("--ado-org", "Azure DevOps organization name");
        var adoPatOption = new Option<string>("--ado-pat", "Azure DevOps Personal Access Token");
        var githubOrgOption = new Option<string>("--gh-org", "GitHub organization name");
        var githubPatOption = new Option<string>("--gh-pat", "GitHub Personal Access Token");

        // Optional options with defaults
        var adoVersionOption = new Option<string>("--ado-version",  "Azure DevOps version (e.g., 2019, 2020, 2022, cloud)");
        var adoBaseUrlOption = new Option<string>("--ado-baseurl",  "Azure DevOps Server base URL");
        var adoProjectsOption = new Option<string>("--ado-projects", "Comma-separated list of project names");
        var repoPatternOption = new Option<string>("--repo-name-pattern", "Pattern for GitHub repository names. Available placeholders: {orgName}, {projectName}, {repoName}");
        var teamPatternOption = new Option<string>("--team-name-pattern", "Pattern for GitHub team names. Available placeholders: {orgName}, {projectName}, {teamName}");
        var migrateTeamsOption = new Option<bool>("--migrate-teams", "Whether to migrate Azure DevOps teams to GitHub teams");
        var skipConfirmationOption = new Option<bool>("--skip-confirmation", () => false, "Skip confirmation prompt before migration");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var usersMappingFileOption = new Option<string>("--users-mapping-file", "Path to the users mapping file");
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");
        var gitDisableSslVerifyOption = new Option<bool>("--git-disable-ssl-verify", "Disable SSL verification for Git operations");
        var usePatForCloneOption = new Option<bool>("--use-pat-for-clone", "Whether to use PAT for authentication during git clone");

        command.AddOption(adoOrgOption);
        command.AddOption(adoPatOption);
        command.AddOption(githubOrgOption);
        command.AddOption(githubPatOption);
        command.AddOption(adoVersionOption);
        command.AddOption(adoBaseUrlOption);
        command.AddOption(adoProjectsOption);
        command.AddOption(repoPatternOption);
        command.AddOption(teamPatternOption);
        command.AddOption(migrateTeamsOption);
        command.AddOption(skipConfirmationOption);
        command.AddOption(workingDirOption);
        command.AddOption(configFilePathOption);
        command.AddOption(usersMappingFileOption);
        command.AddOption(usePatForCloneOption);

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
            var finalGithubOrg = context.ParseResult.GetValueForOption(githubOrgOption) ?? config["GitHub:Organization"];
            var finalGithubPat = context.ParseResult.GetValueForOption(githubPatOption) ?? config["GitHub:Pat"];
            var adoVersion = context.ParseResult.GetValueForOption(adoVersionOption) ?? config["AzureDevOps:Version"] ?? "cloud";
            var adoBaseUrl = context.ParseResult.GetValueForOption(adoBaseUrlOption) ?? config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com";
            var adoProjects = context.ParseResult.GetValueForOption(adoProjectsOption) ?? config["AzureDevOps:Projects"];
            var repoPattern = context.ParseResult.GetValueForOption(repoPatternOption) ?? config["Migration:RepoNamePattern"] ?? "{repoName}";
            var teamPattern = context.ParseResult.GetValueForOption(teamPatternOption) ?? config["Migration:TeamNamePattern"] ?? "{teamName}";
            var migrateTeams = context.ParseResult.GetValueForOption(migrateTeamsOption);
            var migrateTeamsConfig = config["Migration:MigrateTeams"];
            if (!migrateTeams && !string.IsNullOrEmpty(migrateTeamsConfig))
            {
                _ = bool.TryParse(migrateTeamsConfig, out migrateTeams);
            }
            var usersMappingFile = context.ParseResult.GetValueForOption(usersMappingFileOption) ?? config["Migration:UsersMappingFile"];
            var skipConfirmation = context.ParseResult.GetValueForOption(skipConfirmationOption);
            if (!skipConfirmation && bool.TryParse(config["Migration:SkipConfirmation"], out bool skipConfirmationConfig))
            {
                skipConfirmation = skipConfirmationConfig;
            }

            var gitDisableSslVerify = context.ParseResult.GetValueForOption(gitDisableSslVerifyOption);
            if (!gitDisableSslVerify && bool.TryParse(config["Git:DisableSSLVerify"], out bool gitDisableSslVerifyConfig))
            {
                gitDisableSslVerify = gitDisableSslVerifyConfig;
            }
            var gitUsePatForClone = context.ParseResult.GetValueForOption(usePatForCloneOption);
            if (!gitUsePatForClone && bool.TryParse(config["Git:UsePatForClone"], out bool gitUsePatForCloneConfig))
            {
                gitUsePatForClone = gitUsePatForCloneConfig;
            }

        
            // Validate required values
            var missingParameters = new List<string>();
            if (string.IsNullOrEmpty(finalAdoOrg))
                missingParameters.Add("--ado-org (Azure DevOps organization)");
            if (string.IsNullOrEmpty(finalAdoPat))
                missingParameters.Add("--ado-pat (Azure DevOps PAT)");
            if (string.IsNullOrEmpty(finalGithubOrg))
                missingParameters.Add("--gh-org (GitHub organization)");
            if (string.IsNullOrEmpty(finalGithubPat))
                missingParameters.Add("--gh-pat (GitHub PAT)");
            if (string.IsNullOrEmpty(adoBaseUrl) && adoVersion != "cloud") 
                missingParameters.Add("--ado-baseurl");
            if (migrateTeams && string.IsNullOrWhiteSpace(usersMappingFile))
                missingParameters.Add("--users-mapping-file");
            if (missingParameters.Any())
            {
                Logger.LogError($"The following required parameters are missing: {string.Join(", ", missingParameters)}");
                Logger.LogError("Please provide them via command-line options or in the configuration file.");
                return; // Gracefully exit
            }

            try
            {
                // Create HttpClient instances for each service
                var adoHttpClient = new HttpClient();
                var githubHttpClient = new HttpClient();

                var adoUrl = $"{adoBaseUrl}/{finalAdoOrg}";

                // Create service instances
                var adoService = new AzureDevOpsService(adoHttpClient, adoUrl, finalAdoPat!, adoVersion);
                var githubService = new GitHubService(githubHttpClient, finalGithubPat!);
                
                var migrationService = new MigrationService(adoService, githubService);

                Logger.LogInfo("Starting migration assessment...");
                // First assess the projects
                var projects = await migrationService.RunAssessmentAsync(finalAdoOrg, adoProjects, finalGithubOrg!, repoPattern, teamPattern, usersMappingFile!);

                // Calculate migration statistics
                int totalRepos = projects.Sum(p => p.Repos.Count);
                int totalTeams = projects.Sum(p => p.Teams.Count);
                int totalMembers = projects
                    .SelectMany(p => p.Teams)
                    .SelectMany(t => t.Members)
                    .DistinctBy(m => m.UniqueName)
                    .Count();


                Logger.LogInfo("\nMigration Summary:");
                Logger.LogInfo($"Total projects to migrate: {projects.Count}");
                Logger.LogInfo($"Total repositories to migrate: {totalRepos}");
                Logger.LogInfo($"Total teams to migrate: {totalTeams}");
                Logger.LogInfo($"Total members to migrate: {totalMembers}");
                Logger.LogInfo("\nProjects breakdown:");
                foreach (var project in projects)
                {
                    totalMembers = project.Teams
                    .SelectMany(t => t.Members)
                    .DistinctBy(m => m.UniqueName)
                    .Count();
                    Logger.LogInfo($"- {project.Name}: {project.Repos.Count} repos, {project.Teams.Count} teams, {totalMembers} members");
                }

                if (!skipConfirmation)
                {
                    Console.Write("\nDo you want to proceed with the migration? (y/N): ");
                    var response = Console.ReadLine()?.ToLower();
                    if (response != "y" && response != "yes")
                    {
                        Logger.LogInfo("Migration cancelled by user.");
                        return;
                    }
                }                
                Logger.LogInfo("\nStarting migration...");
                // First get the projects from the assessment
                //var projectsToMigrate = await migrationService.RunAssessmentAsync(finalAdoOrg, adoProjects, finalGithubOrg!, repoPattern, teamPattern, usersMappingFile!);
                
                // Then migrate the projects
                await migrationService.MigrateAsync(projects, migrateTeams, workingDir, gitDisableSslVerify, gitUsePatForClone);
                Logger.LogInfo("Generating report...");
                var migrationProjects = await migrationService.RunAssessmentAsync(finalAdoOrg, adoProjects, finalGithubOrg!, repoPattern, teamPattern, usersMappingFile!);
                var reportFile = $"{workingDir}\\{DateTime.Now:yyyyMMddHHmm}_{finalAdoOrg}_migration_report.md";
                MigrationReport.GenerateMarkdownReport(migrationProjects, finalAdoOrg!, adoUrl, finalGithubOrg!, reportFile);
                Logger.LogInfo($"Report generated successfully. Report saved to {reportFile}");
                Logger.LogSuccess("Migration completed successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Migration failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}