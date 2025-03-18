using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Models.GitHub;
using System.Runtime.CompilerServices;

/// <summary>
/// Main program class for Azure DevOps to GitHub migration tool
/// </summary>
public class Program
{
    #region Constants
    private const string ExitCommand = "exit";
    private const string DefaultApiVersion = "7.1";
    private const string DefaultCloudBaseUrl = "https://dev.azure.com";
    private const string CsvHeaderDisplayName = "DisplayName";
    private const string CsvHeaderUpn = "UPN";
    private const string CsvHeaderGitHub = "GitHub";
    private const string DefaultConfigFile = "migrator_config.json";
    #endregion

    /// <summary>
    /// Entry point of the application
    /// </summary>
    /// <param name="args">Command line arguments</param>
    static async Task Main(string[] args)
    {
        var configPath = GetConfigPath(args);
        if (!File.Exists(configPath))
        {
            Logger.LogError($"Configuration file not found: {configPath}");
            Logger.LogError($"Please ensure {DefaultConfigFile} exists or specify a config file with --config");
            return;
        }

        if (args.Length == 0)
        {
            await RunInteractiveMode();
            return;
        }

        await ExecuteCommand(args);
    }

    /// <summary>
    /// Gets the configuration file path from command line arguments or uses default
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configuration file path</returns>
    private static string GetConfigPath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].ToLower() == "--config")
            {
                return args[i + 1];
            }
        }
        return DefaultConfigFile;
    }

    /// <summary>
    /// Runs the application in interactive mode, allowing users to input commands
    /// </summary>
    private static async Task RunInteractiveMode()
    {
        while (true)
        {
            ShowUsage();
            var userInput = Logger.PromptForInput($"\nEnter a command (or '{ExitCommand}' to quit):");

            if (string.IsNullOrEmpty(userInput) || userInput.ToLower() == ExitCommand)
                break;

            var args = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            await ExecuteCommand(args);

            Logger.WaitForEnter();
            Logger.ClearScreen();
        }
    }

    /// <summary>
    /// Prompts the user for a parameter value
    /// </summary>
    /// <param name="paramName">Parameter name</param>
    /// <param name="description">Parameter description</param>
    /// <returns>Parameter value entered by the user</returns>
    public static string PromptForParameter(string paramName, string description)
    {
        return Logger.PromptForInput($"\nEnter {paramName}: {description}");
    }

    /// <summary>
    /// Executes the specified command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task ExecuteCommand(string[] args)
    {
        if (args.Length > 1 && args.Contains("--help"))
        {
            ShowCommandHelp(args[0].ToLower());
            return;
        }

        try
        {
            switch (args[0].ToLower())
            {
                case "check-git":
                    await CheckGitCommandAsync();
                    break;

                case "check-tfvc":
                    await CheckTfvcCommandAsync();
                    break;

                case "install-git":
                    await InstallGitCommandAsync();
                    break;

                case "install-gittfs":
                    await InstallGitTfsCommandAsync();
                    break;

                case "ado-assessment":
                    await AssessmentCommandAsync(args);
                    break;

                case "export-repo":
                    await ExportRepoCommandAsync(args);
                    break;

                case "export-members":
                    await ExportMembersCommandAsync();
                    break;

                case "migration-report":
                    await GenerateReportCommandAsync(args);
                    break;

                case "ado-export-users":
                    await ExportUsersCommandAsync(args);
                    break;

                case "gh-export-users":
                    await ExportGithubSamlUsersCommandAsync(args);
                    break;

                case "ado-migrate":
                    await MigrateCommandAsync(args);
                    break;

                default:
                    Logger.LogWarning($"Unknown command: {args[0]}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Checks if Git is installed
    /// </summary>
    private static async Task CheckGitCommandAsync()
    {
        await RequiredModulesChecker.VerifyGitAsync();
    }

    /// <summary>
    /// Checks if Git-TFS is installed
    /// </summary>
    private static async Task CheckTfvcCommandAsync()
    {
        await GitTfsInstaller.VerifyGitTfsInstallationAsync();
    }

    /// <summary>
    /// Installs Git
    /// </summary>
    private static async Task InstallGitCommandAsync()
    {
        if (await GitInstaller.InstallGitAsync())
            Logger.LogSuccess("Git installation completed successfully.");
        else
            Logger.LogError("Git installation failed.");
    }

    /// <summary>
    /// Installs Git-TFS
    /// </summary>
    private static async Task InstallGitTfsCommandAsync()
    {
        if (await GitTfsInstaller.InstallGitTfsAsync())
            Logger.LogSuccess("Git-TFS installation completed successfully.");
        else
            Logger.LogError("Git-TFS installation failed.");
    }

    /// <summary>
    /// Runs the assessment command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task AssessmentCommandAsync(string[] args)
    {
        using var httpClient = new HttpClient();
        var parameters = CommandParameters.CreateAssessmentParameters();
        parameters.CollectParameters(args);

        var adoVersion = parameters.GetValue("version");
        var baseUrl = parameters.GetValue("baseUrl");
        var adoOrg = parameters.GetValue("organization");
        var adoPat = parameters.GetValue("pat");

        var apiVersion = GetAzureDevopsApiVersion(adoVersion);

        string[]? projectNames = null;
        if (parameters.TryGetValue("project", out var projectValue) && !string.IsNullOrEmpty(projectValue))
        {
            projectNames = projectValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var assessmentService = new AssessmentService(httpClient, adoOrg, adoPat, baseUrl, apiVersion);
        try
        {
            Logger.LogInfo("Running assessment...");
            var projects = await assessmentService.AssessAsync(projectNames);
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
            var jsonFile = $"{timestamp}_{adoOrg}_assessment.json";
            var json = System.Text.Json.JsonSerializer.Serialize(projects, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonFile, json);
            Logger.LogSuccess($"Assessment completed. Report saved to {jsonFile}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Assessment failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs the export repository command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task ExportRepoCommandAsync(string[] args)
    {
        var parameters = CommandParameters.CreateExportRepoParameters();
        parameters.CollectParameters(args);

        var success = await RepositoryMigrator.MigrateRepositoryContentAsync(
            parameters.GetValue("sourceRepo"),
            parameters.GetValue("githubOrg"),
            parameters.GetValue("githubPat"),
            parameters.GetValue("name"),
            parameters.GetValue("projects"),
            parameters.GetValue("pat"),
            bool.Parse(parameters.GetValue("tfvc"))
        );

        if (success)
            Logger.LogSuccess("Repository migration completed successfully.");
        else
            Logger.LogError("Repository migration failed.");
    }

    /// <summary>
    /// Runs the export members command
    /// </summary>
    private static Task ExportMembersCommandAsync()
    {
        Logger.LogWarning("Command 'export-members' is not implemented yet.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the generate report command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task GenerateReportCommandAsync(string[] args)
    {
        var parameters = CommandParameters.CreateMigrationReportParameters();
        parameters.CollectParameters(args);

        var adoVersion = parameters.GetValue("version");
        var baseUrl = parameters.GetValue("baseUrl");
        var adoOrg = parameters.GetValue("organization");
        var adoPat = parameters.GetValue("pat");
        var projects = parameters.GetValue("projects");
        var githubOrg = parameters.GetValue("githubOrg");
        var githubPat = parameters.GetValue("githubPat");
        var repoNamePattern = parameters.GetValue("repoNamePattern");
        var teamNamePattern = parameters.GetValue("teamNamePattern");
        var outputFile = parameters.GetValue("output") ?? $"{DateTime.Now:yyyyMMddHHmm}_{adoOrg}_migration_report.md";

        var apiVersion = GetAzureDevopsApiVersion(adoVersion);
        var adoUrl = $"{baseUrl}/{adoOrg}";

        using var httpClient = new HttpClient();
        var adoService = new AzureDevOpsService(httpClient, adoUrl, adoPat, apiVersion);
        using var httpClientGH = new HttpClient();
        var githubService = new GitHubService(httpClientGH, githubPat);

        Logger.LogInfo("Running assessment...");
        var migrationService = new MigrationService(adoService, githubService);
        var migrationProjects = await migrationService.RunAssessmentAsync(projects, githubOrg, repoNamePattern, teamNamePattern);

        MigrationReport.GenerateMarkdownReport(migrationProjects, adoOrg, adoUrl, githubOrg, outputFile);
        Logger.LogSuccess("Assessment report generated successfully.");
        return;
    }

    /// <summary>
    /// Runs the export users command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task ExportUsersCommandAsync(string[] args)
    {
        var parameters = CommandParameters.CreateExportUsersParameters();
        parameters.CollectParameters(args);

        var adoVersion = parameters.GetValue("version");
        var baseUrl = parameters.GetValue("baseUrl");
        var adoOrg = parameters.GetValue("organization");
        var adoPat = parameters.GetValue("pat");
        var projects = parameters.GetValue("projects");

        var apiVersion = GetAzureDevopsApiVersion(adoVersion);

        var url = $"{baseUrl}/{adoOrg}";
        var httpClient = new HttpClient();
        var adoService = new AzureDevOpsService(httpClient, url, adoPat, apiVersion);
        try
        {
            Logger.LogInfo($"Extracting users from {(string.IsNullOrEmpty(projects) ? "all projects" : $"project '{projects}'")}...");

            var users = await adoService.ExtractUsersAsync(projects);
            if (!users.Any())
            {
                Logger.LogWarning("No users found to export.");
                return;
            }

            // Create CSV with proper escaping and formatting
            var csvLines = new List<string> { $"{CsvHeaderDisplayName},{CsvHeaderUpn},{CsvHeaderGitHub}" };
            csvLines.AddRange(users.Select(u =>
                $"{EscapeCsvField(u.Identity.DisplayName ?? "")},{EscapeCsvField(u.Identity.UniqueName ?? "")},"
            ));

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
            var outputFile = $"{timestamp}_{adoOrg}_users.csv";

            await File.WriteAllLinesAsync(outputFile, csvLines);
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
    }

    /// <summary>
    /// Runs the export GitHub users command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task ExportGithubSamlUsersCommandAsync(string[] args)
    {
        var parameters = CommandParameters.CreateGitHubSamlUsersParameters();
        parameters.CollectParameters(args);

        var organization = parameters.GetValue("organization");
        var pat = parameters.GetValue("pat");

        using var httpClient = new HttpClient();
        var githubService = new GitHubService(httpClient, token: pat);

        try
        {
            Logger.LogInfo($"Exporting users from GitHub organization: {organization}");
            
            // Check if SAML is enabled
            var hasSaml = await githubService.HasSamlEnabledAsync(organization);
            
            // Get organization users
            var users = await githubService.GetOrgUsersAsync(organization, string.Empty);
            List<SAMLUserIdentity> samlUsers = new List<SAMLUserIdentity>();

            if (hasSaml)
            {
                Logger.LogInfo("SAML SSO is enabled - fetching SAML identities...");
                samlUsers = await githubService.GetSamlIdentitiesAsync(organization, string.Empty);
            }

            if (!users.Any())
            {
                Logger.LogWarning("No users found to export.");
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
            var outputFile = $"{timestamp}_{organization}_users.csv";

            var csvLines = new List<string>();
            if (hasSaml)
            {
                csvLines.Add("GitHubLogin,GitHubName,GitHubEmail,SAMLIdentity,OrgRole");
                foreach (var user in users)
                {
                    var samlIdentity = samlUsers.FirstOrDefault(s => s.Login == user.Login)?.SamlIdentity ?? "";
                    var line = $"{EscapeCsvField(user.Login ?? "")},{EscapeCsvField(user.Name ?? "")},{EscapeCsvField(user.Email ?? "")},{EscapeCsvField(samlIdentity)},{EscapeCsvField(user.OrgRole ?? "")}";
                    csvLines.Add(line);
                }
            }
            else 
            {
                csvLines.Add("GitHubLogin,GitHubName,GitHubEmail,OrgRole");
                foreach (var user in users)
                {
                    var line = $"{EscapeCsvField(user.Login ?? "")},{EscapeCsvField(user.Name ?? "")},{EscapeCsvField(user.Email ?? "")},{EscapeCsvField(user.OrgRole ?? "")}";
                    csvLines.Add(line);
                }
            }

            await File.WriteAllLinesAsync(outputFile, csvLines);
            Logger.LogSuccess($"\nSuccessfully exported {users.Count()} users to: {outputFile}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export users: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Runs the migrate command
    /// </summary>
    /// <param name="args">Command line arguments</param>
    private static async Task MigrateCommandAsync(string[] args)
    {
        var parameters = CommandParameters.CreateMigrationParameters();
        parameters.CollectParameters(args);

        var adoVersion = parameters.GetValue("version");
        var baseUrl = parameters.GetValue("baseUrl");
        var adoOrg = parameters.GetValue("organization");
        var adoPat = parameters.GetValue("pat");
        var projects = parameters.GetValue("projects");
        var githubOrg = parameters.GetValue("githubOrg");
        var githubPat = parameters.GetValue("githubPat");
        var migrateTeams = parameters.GetValue("migrateTeams").ToLower() == "true";
        var dryRun = parameters.GetValue("dryRun").ToLower() == "true";
        var repoNamePattern = parameters.GetValue("repoNamePattern");
        var teamNamePattern = parameters.GetValue("teamNamePattern");
        var defaultTeamMemberRole = parameters.GetValue("defaultTeamMemberRole");

        var apiVersion = GetAzureDevopsApiVersion(adoVersion);
        var adoUrl = $"{baseUrl}/{adoOrg}";

        using var httpClient = new HttpClient();
        var adoService = new AzureDevOpsService(httpClient, adoUrl, adoPat, apiVersion);
        using var httpClientGH = new HttpClient();
        var githubService = new GitHubService(
            httpClientGH, 
            githubPat, 
            defaultTeamMemberRole: defaultTeamMemberRole);

        try
        {
            // First run an assessment
            Logger.LogInfo("Running pre-migration assessment...");
            var migrationService = new MigrationService(adoService, githubService);
            var migrationProjects = await migrationService.RunAssessmentAsync(projects, githubOrg, repoNamePattern, teamNamePattern);

            if (dryRun)
            {
                Logger.LogInfo("Dry run mode - Migration skipped");
                MigrationReport.GenerateMarkdownReport(migrationProjects, adoOrg, adoUrl, githubOrg, $"{DateTime.Now:yyyyMMddHHmm}_{adoOrg}_migration_report.md");
                Logger.LogSuccess("Assessment report generated successfully.");
                return;
            }

            // Confirm with user before proceeding
            if (!Logger.PromptForConfirmation("\nDo you want to proceed with the migration? Type 'yes' to continue: "))
            {
                Logger.LogInfo("Migration cancelled by user");
                return;
            }

            // Migrate repositories
            Logger.LogInfo("\nStarting repository migration...");

            foreach (var proj in migrationProjects)
            {
                if (proj.Name == null)
                {
                    Logger.LogWarning("Skipping project with no name");
                    continue;
                }

                var repos = proj.Repos.Where(r => r.GitHubRepoMigrationStatus != MigrationStatus.Completed
                    && r.GitHubRepoMigrationStatus != MigrationStatus.Skipped).ToList();

                foreach (var repo in repos)
                {
                    if (repo.Name == null || repo.GitHubRepoName == null || repo.Url == null)
                    {
                        Logger.LogWarning($"Skipping repository with missing required information in project: {proj.Name}");
                        continue;
                    }

                    Logger.LogInfo($"Migrating repository: {repo.Name} from project: {proj.Name}");
                    var repoExists = await githubService.RepositoryExistsAsync(githubOrg, repo.GitHubRepoName);
                    if (!repoExists)
                    {
                        Logger.LogInfo($"Repository {repo.GitHubRepoName} does not exist in GitHub. Creating it...");
                        var repoCreated = await githubService.CreateRepositoryAsync(githubOrg, repo.GitHubRepoName, proj.Url, true);
                        if (repoCreated?.Id != null)
                        {
                            Logger.LogSuccess($"Repository {repo.GitHubRepoName} created successfully in GitHub.");
                        }
                        else
                        {
                            Logger.LogError($"Failed to create repository {repo.GitHubRepoName} in GitHub.");
                            continue; // Skip to the next repository if creation fails
                        }
                    }

                    var repoMigrated = await RepositoryMigrator.MigrateRepositoryContentAsync(
                        repo.Url,
                        githubOrg,
                        githubPat,
                        repo.GitHubRepoName,
                        proj.Name,
                        adoPat,
                        isTfvc: repo.RepositoryType.Equals("tfvc", StringComparison.OrdinalIgnoreCase)
                    );

                    if (repoMigrated)
                    {
                        Logger.LogSuccess($"Successfully migrated repository: {repo.Name}");
                        repo.GitHubRepoMigrationStatus = MigrationStatus.Completed;
                    }
                    else
                    {
                        Logger.LogError($"Failed to migrate repository: {repo.Name}");
                        repo.GitHubRepoMigrationStatus = MigrationStatus.Failed;
                    }
                }
            }

            // After repository migration, handle teams if enabled
            if (migrateTeams)
            {
                Logger.LogInfo("\nStarting team migration...");
                // Teams migration logic will be handled by MigrationService
            }

            Logger.LogSuccess("\nMigration completed successfully!");
            MigrationReport.GenerateMarkdownReport(migrationProjects, adoOrg, adoUrl, githubOrg, $"{DateTime.Now:yyyyMMddHHmm}_{adoOrg}_migration_report.md");
            Logger.LogSuccess("Assessment report generated successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Migration failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Exports the repository
    /// </summary>
    /// <param name="sourceRepo">Source repository</param>
    /// <param name="githubOrg">GitHub organization</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="projectName">Project name</param>
    /// <param name="adoPat">Azure DevOps Personal Access Token</param>
    /// <param name="isTfvc">Indicates if the repository is TFVC</param>
    /// <returns>True if the export was successful, otherwise false</returns>
    private static async Task<bool> ExportRepositoryAsync(string sourceRepo, string githubOrg, string githubPat, string repoName, string projectName, string adoPat, bool isTfvc)
    {
        try
        {
            return await RepositoryMigrator.MigrateRepositoryContentAsync(
                sourceRepo,
                githubOrg,
                githubPat,
                repoName,
                projectName,
                adoPat,
                isTfvc
            );
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export repository: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Escapes a CSV field
    /// </summary>
    /// <param name="field">Field value</param>
    /// <returns>Escaped field value</returns>
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    /// <summary>
    /// Shows the usage information
    /// </summary>
    private static void ShowUsage()
    {
        Logger.LogInfo("Available commands:");
        Logger.LogInfo("  check-git                Check if Git is installed");
        Logger.LogInfo("  check-tfvc               Check if Git-TFS is installed");
        Logger.LogInfo("  install-git              Install Git");
        Logger.LogInfo("  install-gittfs           Install Git-TFS");
        Logger.LogInfo("  ado-assessment           Run assessment");
        Logger.LogInfo("  ado-export-users         Extract Azure DevOps users to CSV file");
        Logger.LogInfo("  gh-export-users          Export GitHub organization users to CSV file");
        Logger.LogInfo("  migration-report         Generate migration report");
        Logger.LogInfo("\nUse 'command --help' to see detailed usage instructions (e.g., 'ado-assessment --help')");
    }

    /// <summary>
    /// Shows the command help information
    /// </summary>
    /// <param name="command">Command name</param>
    private static void ShowCommandHelp(string command)
    {
        switch (command)
        {
            case "ado-assessment":
                Logger.LogInfo("Assessment command parameters:");
                Logger.LogInfo("Required:");
                Logger.LogInfo("  --ado-org          Azure DevOps organization name");
                Logger.LogInfo("  --ado-pat          Azure DevOps Personal Access Token");
                Logger.LogInfo("  --ado-projects     Comma-separated list of project names, leave empty for all projects");
                Logger.LogInfo("\nOptional:");
                Logger.LogInfo("  --ado-version      Azure DevOps version (e.g., 2019, 2020), default: cloud");
                Logger.LogInfo("  --ado-baseurl      Azure DevOps Server base URL, default: https://dev.azure.com");
                break;

            case "ado-export-users":
                Logger.LogInfo("Export users command parameters:");
                Logger.LogInfo("Required:");
                Logger.LogInfo("  --ado-org          Azure DevOps organization name");
                Logger.LogInfo("  --ado-pat          Azure DevOps Personal Access Token");
                Logger.LogInfo("  --ado-projects     Comma-separated list of project names, leave empty for all projects");
                Logger.LogInfo("\nOptional:");
                Logger.LogInfo("  --ado-version      Azure DevOps version (e.g., 2019, 2020), default: cloud");
                Logger.LogInfo("  --ado-baseurl      Azure DevOps Server base URL, default: https://dev.azure.com");
                Logger.LogInfo("  --output           Output file path, default: user-mapping.csv");
                break;

            case "gh-export-users":
                Logger.LogInfo("Export GitHub users command parameters:");
                Logger.LogInfo("Required:");
                Logger.LogInfo("  --gh-org           GitHub organization name");
                Logger.LogInfo("  --gh-pat           GitHub Personal Access Token with admin:org scope");
                Logger.LogInfo("\nNote: If SAML SSO is enabled for the organization, SAML identity information will be included in the export");
                break;

            case "ado-migrate":
                Logger.LogInfo("Migration command parameters:");
                Logger.LogInfo("Required:");
                Logger.LogInfo("  --ado-org          Azure DevOps organization name");
                Logger.LogInfo("  --ado-pat          Azure DevOps Personal Access Token");
                Logger.LogInfo("  --gh-org           GitHub organization name");
                Logger.LogInfo("  --gh-pat           GitHub Personal Access Token");
                Logger.LogInfo("\nOptional:");
                Logger.LogInfo("  --ado-version      Azure DevOps version (e.g., 2019, 2020), default: cloud");
                Logger.LogInfo("  --ado-baseurl      Azure DevOps Server base URL, default: https://dev.azure.com");
                Logger.LogInfo("  --ado-projects     Comma-separated list of project names");
                Logger.LogInfo("  --repo-pattern     Pattern for GitHub repository names, default: {projectName}-{repoName}");
                Logger.LogInfo("  --team-pattern     Pattern for GitHub team names, default: {projectName}-{teamName}");
                Logger.LogInfo("  --users-mapping    Path to CSV file containing ADO to GitHub users mapping");
                Logger.LogInfo("  --migrate-teams    Whether to migrate teams (true/false), default: false");
                Logger.LogInfo("  --dry-run          Run assessment only without migrating (true/false), default: false");
                Logger.LogInfo("  --team-role        Default GitHub team member role, default: member");
                break;

            case "migration-report":
                Logger.LogInfo("Migration report command parameters:");
                Logger.LogInfo("Required:");
                Logger.LogInfo("  --ado-org          Azure DevOps organization name");
                Logger.LogInfo("  --ado-pat          Azure DevOps Personal Access Token");
                Logger.LogInfo("  --gh-org           GitHub organization name");
                Logger.LogInfo("  --gh-pat           GitHub Personal Access Token");
                Logger.LogInfo("\nOptional:");
                Logger.LogInfo("  --ado-version      Azure DevOps version (e.g., 2019, 2020), default: cloud");
                Logger.LogInfo("  --ado-baseurl      Azure DevOps Server base URL, default: https://dev.azure.com");
                Logger.LogInfo("  --ado-projects     Comma-separated list of project names");
                Logger.LogInfo("  --repo-pattern     Pattern for GitHub repository names, default: {projectName}-{repoName}");
                Logger.LogInfo("  --team-pattern     Pattern for GitHub team names, default: {projectName}-{teamName}");
                break;

            case "check-git":
            case "check-tfvc":
            case "install-git":
            case "install-gittfs":
                Logger.LogInfo($"{command}: No parameters required");
                break;

            default:
                Logger.LogWarning($"Unknown command: {command}");
                ShowUsage();
                break;
        }
    }

    /// <summary>
    /// Gets the Azure DevOps API version based on the specified version
    /// </summary>
    /// <param name="adoVersion">Azure DevOps version</param>
    /// <returns>API version</returns>
    private static string GetAzureDevopsApiVersion(string adoVersion)
    {
        return adoVersion switch
        {
            "2015" => "2.0",
            "2017" => "3.0",
            "2018" => "4.1",
            "2019" => "5.1",
            "2020" => "6.0",
            "2022" => "6.0",
            _ => DefaultApiVersion
        };
    }
}
