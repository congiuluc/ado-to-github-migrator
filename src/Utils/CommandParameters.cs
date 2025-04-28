using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Utils
{
    public class Parameter
    {
        public string Name { get; }
        public string Description { get; }
        public string Flag { get; }
        public bool IsRequired { get; }
        public string DefaultValue { get; }
        public Func<string, bool>? Validator { get; }
        public string ConfigPath { get; }

        public Parameter(string name, string description, string flag, bool isRequired = true, string defaultValue = "", 
            Func<string, bool>? validator = null, string configPath = "")
        {
            Name = name;
            Description = description;
            Flag = flag;
            IsRequired = isRequired;
            DefaultValue = defaultValue;
            Validator = validator;
            ConfigPath = configPath;
        }
    }

    /// <summary>
    /// Handles parsing and validation of command-line parameters for the migration tool.
    /// Provides strongly-typed access to command-line arguments with validation.
    /// </summary>
    /// <remarks>
    /// This class ensures that:
    /// - Required parameters are provided
    /// - Parameters are in the correct format
    /// - Default values are applied where appropriate
    /// - Invalid combinations are detected early
    /// </remarks>
    public class CommandParameters
    {
        /// <summary>
        /// The Azure DevOps organization URL (e.g., https://dev.azure.com/organization)
        /// For Azure DevOps Server, use the server URL (e.g., https://tfs.company.com/tfs)
        /// </summary>
        public string? AzureDevOpsUrl { get; private set; }

        /// <summary>
        /// Personal Access Token for Azure DevOps authentication
        /// </summary>
        /// <remarks>
        /// Required scopes:
        /// - Code (read)
        /// - Project and Team (read)
        /// - Identity (read)
        /// - Member Entitlement Management (read)
        /// - Graph (read)
        /// Token must have access to all projects being migrated
        /// </remarks>
        public string? AzureDevOpsPat { get; private set; }

        /// <summary>
        /// The GitHub organization name where repositories will be migrated
        /// This should be just the organization name, not the full URL
        /// </summary>
        /// <remarks>
        /// Ensure the organization is properly configured:
        /// - SAML SSO if required
        /// - Appropriate repository and team creation permissions
        /// - Sufficient repository quotas
        /// </remarks>
        public string? GitHubOrg { get; private set; }

        /// <summary>
        /// Personal Access Token for GitHub authentication
        /// </summary>
        /// <remarks>
        /// Required scopes:
        /// - repo (full control of private repositories)
        /// - workflow (manage GitHub Actions)
        /// - admin:org (manage organization settings)
        /// - delete_repo (delete repositories)
        /// 
        /// Token must be created by an organization owner or admin with sufficient permissions.
        /// For organizations with SAML SSO, the token must be authorized for SSO access.
        /// </remarks>
        public string? GitHubPat { get; private set; }

        /// <summary>
        /// Optional path to a configuration file with additional settings
        /// </summary>
        public string? ConfigPath { get; private set; }

        /// <summary>
        /// Maximum number of parallel migration operations
        /// </summary>
        public int MaxParallelOperations { get; private set; } = 4;

        /// <summary>
        /// Whether to perform a dry run without making actual changes
        /// </summary>
        public bool DryRun { get; private set; }

        private readonly Dictionary<string, string> _parameters = new();
        private readonly List<Parameter> _parameterDefinitions;
        private static IConfiguration? _configuration;

        public CommandParameters(IEnumerable<Parameter> parameters)
        {
            _parameterDefinitions = parameters.ToList();
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            if (_configuration == null)
            {
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory);

                // First try custom config path if provided
                if (_parameters.TryGetValue("config", out var customConfig) && File.Exists(customConfig))
                {
                    Logger.LogInfo($"Loading configuration from {customConfig}");
                    configBuilder.AddJsonFile(customConfig, optional: false);
                }
                else
                {
                    // Fall back to default config file
                    var defaultConfig = "migrator_config.json";
                    if (File.Exists(defaultConfig))
                    {
                        Logger.LogInfo($"Loading default configuration from {defaultConfig}");
                        configBuilder.AddJsonFile(defaultConfig, optional: true);
                    }
                    else
                    {
                        Logger.LogWarning($"No configuration file found at {defaultConfig}");
                    }
                }

                _configuration = configBuilder.Build();
            }
        }

        public string GetValue(string paramName)
        {
            var result = _parameters.TryGetValue(paramName, out var value) ? value : string.Empty;
            return result.Replace("\"","");
        }

        public bool TryGetValue(string paramName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            return _parameters.TryGetValue(paramName, out value);
        }

        public void CollectParameters(string[] args)
        {
            foreach (var param in _parameterDefinitions)
            {
                var flagIndex = Array.IndexOf(args, param.Flag);
                string? value = null;

                // First try command line args
                if (flagIndex != -1 && flagIndex + 1 < args.Length)
                {
                    value = args[flagIndex + 1];
                    Logger.LogInfo($"Using command-line value for {param.Name}: {value}");
                }
                
                // If not in command line, try config file
                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(param.ConfigPath))
                {
                    value = _configuration?[param.ConfigPath];
                    if (!string.IsNullOrEmpty(value))
                    {
                        Logger.LogInfo($"Using configuration value for {param.Name} from {param.ConfigPath}");
                    }
                }

                // If still not found and required, prompt user
                if (string.IsNullOrEmpty(value) && param.IsRequired)
                {
                    Logger.LogWarning($"Required parameter {param.Name} not found in command line or configuration");
                    //value = Program.PromptForParameter(param.Name, param.Description) ?? string.Empty;
                }

                // If still empty, use default value
                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(param.DefaultValue))
                {
                    value = param.DefaultValue;
                    Logger.LogInfo($"Using default value for {param.Name}: {value}");
                }

                // Validate if we have a validator
                if (!string.IsNullOrEmpty(value) && param.Validator != null && !param.Validator(value))
                {
                    var errorMessage = $"Invalid value for parameter {param.Name}: {value}";
                    Logger.LogError(errorMessage);
                    throw new ArgumentException(errorMessage);
                }

                if (!string.IsNullOrEmpty(value))
                {
                    _parameters[param.Name] = value;
                }
                else if (param.IsRequired)
                {
                    var errorMessage = $"Required parameter {param.Name} not provided";
                    Logger.LogError(errorMessage);
                    throw new ArgumentException(errorMessage);
                }
            }
        }

        public static CommandParameters CreateAssessmentParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("version", "Azure DevOps version (e.g., 2019, 2020), leave empty for Azure DevOps Services", 
                    "--ado-version", false, "cloud", null, "AzureDevOps:Version"),
                new Parameter("baseUrl", "Azure DevOps Server base URL (e.g., https://tfs.company.com/tfs), leave empty for Azure DevOps Services", 
                    "--ado-baseurl", false, "https://dev.azure.com", null, "AzureDevOps:BaseUrl"),
                new Parameter("organization", "Azure DevOps organization name", 
                    "--ado-org", true, "", null, "AzureDevOps:Organization"),
                new Parameter("pat", "Azure DevOps Personal Access Token", 
                    "--ado-pat", true, "", null, "AzureDevOps:Pat"),
                new Parameter("projects", "Comma-separated list of project names, leave empty for all projects", 
                    "--ado-projects", true)
            });
        }

        public static CommandParameters CreateExportUsersParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("version", "Azure DevOps version (e.g., 2019, 2020, 2022), leave empty for Azure DevOps Services", 
                    "--ado-version", false, "cloud", null, "AzureDevOps:Version"),
                new Parameter("baseUrl", "Azure DevOps Server base URL (e.g., https://tfs.company.com/tfs), leave empty for Azure DevOps Services", 
                    "--ado-baseurl", false, "https://dev.azure.com", null, "AzureDevOps:BaseUrl"),
                new Parameter("organization", "Azure DevOps organization name", 
                    "--ado-org", true, "", null, "AzureDevOps:Organization"),
                new Parameter("pat", "Azure DevOps Personal Access Token", 
                    "--ado-pat", true, "", null, "AzureDevOps:Pat"),
                new Parameter("projects", "Comma-separated list of project names, leave empty for all projects", 
                    "--ado-projects", true),
                new Parameter("output", "Output file path", 
                    "--output", false, "user-mapping.csv")
            });
        }

        public static CommandParameters CreateExportRepoParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("sourceRepo", "Source repository URL", 
                    "--source", true),
                new Parameter("githubOrg", "GitHub organization name", 
                    "--gh-org", true, "", null, "GitHub:Organization"),
                new Parameter("githubPat", "GitHub Personal Access Token", 
                    "--gh-pat", true, "", null, "GitHub:Pat"),
                new Parameter("name", "Repository name", 
                    "--name", true),
                new Parameter("projects", "Project name", 
                    "--projects", true),
                new Parameter("pat", "Azure DevOps Personal Access Token", 
                    "--pat", true, "", null, "AzureDevOps:Pat"),
                new Parameter("tfvc", "Is TFVC repository", 
                    "--tfvc", false, "false", value => value.ToLower() == "true" || value.ToLower() == "false")
            });
        }

        public static CommandParameters CreateGitHubSamlUsersParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("organization", "GitHub organization name", 
                    "--gh-org", true, "", null, "GitHub:Organization"),
                new Parameter("pat", "GitHub Personal Access Token with admin:org scope", 
                    "--gh-pat", true, "", null, "GitHub:Pat")
            });
        }

        public static CommandParameters CreateMigrationParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("version", "Azure DevOps version (e.g., 2019, 2020), leave empty for Azure DevOps Services", 
                    "--ado-version", false, "cloud", null, "AzureDevOps:Version"),
                new Parameter("baseUrl", "Azure DevOps Server base URL (e.g., https://tfs.company.com/tfs), leave empty for Azure DevOps Services", 
                    "--ado-baseurl", false, "https://dev.azure.com", null, "AzureDevOps:BaseUrl"),
                new Parameter("organization", "Azure DevOps organization name", 
                    "--ado-org", true, "", null, "AzureDevOps:Organization"),
                new Parameter("pat", "Azure DevOps Personal Access Token", 
                    "--ado-pat", true, "", null, "AzureDevOps:Pat"),
                new Parameter("projects", "Comma-separated list of project names, leave empty for all projects", 
                    "--ado-projects", false),
                new Parameter("githubOrg", "GitHub organization name", 
                    "--gh-org", true, "", null, "GitHub:Organization"),
                new Parameter("githubPat", "GitHub Personal Access Token", 
                    "--gh-pat", true, "", null, "GitHub:Pat"),
                new Parameter("migrateTeams", "Include team migration (true/false)", 
                    "--migrate-teams", false, "false", null, "Migration:MigrateTeams"),
                new Parameter("dryRun", "Run assessment only without migrating (true/false)", 
                    "--dry-run", false, "false", null, "Migration:DryRun"),
                new Parameter("repoNamePattern", "Pattern for GitHub repository names", 
                    "--repo-pattern", false, "{projectName}-{repoName}", null, "Migration:RepoNamePattern"),
                new Parameter("teamNamePattern", "Pattern for GitHub team names", 
                    "--team-pattern", false, "{projectName}-{teamName}", null, "Migration:TeamNamePattern"),
                new Parameter("usersMapping", "Path to CSV file containing ADO to GitHub users mapping", 
                    "--users-mapping", false, "", null, "Migration:UsersMappingFile"),
                new Parameter("defaultTeamMemberRole", "Default role for team members (member/maintainer)", 
                    "--team-member-role", false, "member", null, "GitHub:DefaultTeamMemberRole")
            });
        }

        public static CommandParameters CreateMigrationReportParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("version", "Azure DevOps version (e.g., 2019, 2020), leave empty for Azure DevOps Services", 
                    "--ado-version", false, "cloud", null, "AzureDevOps:Version"),
                new Parameter("baseUrl", "Azure DevOps Server base URL (e.g., https://tfs.company.com/tfs), leave empty for Azure DevOps Services", 
                    "--ado-baseurl", false, "https://dev.azure.com", null, "AzureDevOps:BaseUrl"),
                new Parameter("organization", "Azure DevOps organization name", 
                    "--ado-org", true, "", null, "AzureDevOps:Organization"),
                new Parameter("pat", "Azure DevOps Personal Access Token", 
                    "--ado-pat", true, "", null, "AzureDevOps:Pat"),
                new Parameter("projects", "Comma-separated list of project names, leave empty for all projects", 
                    "--ado-projects", false),
                new Parameter("githubOrg", "GitHub organization name", 
                    "--gh-org", true, "", null, "GitHub:Organization"),
                new Parameter("githubPat", "GitHub Personal Access Token", 
                    "--gh-pat", true, "", null, "GitHub:Pat"),
                new Parameter("scope", "Scope value for repository and team naming", 
                    "--scope", false, "mig", null, "Migration:Scope"),
                new Parameter("prefix", "Prefix value for repository and team naming", 
                    "--prefix", false, "azdo", null, "Migration:Prefix"),
                new Parameter("repoNamePattern", "Pattern for GitHub repository names", 
                    "--repo-pattern", false, "{projectName}-{repoName}", null, "Migration:RepoNamePattern"),
                new Parameter("teamNamePattern", "Pattern for GitHub team names", 
                    "--team-pattern", false, "{projectName}-{teamName}", null, "Migration:TeamNamePattern")
            });
        }

        public static CommandParameters CreateCommonParameters()
        {
            return new CommandParameters(new[]
            {
                new Parameter("config", "Configuration file path", "--config", false)
            });
        }

        /// <summary>
        /// Creates a new instance of CommandParameters with validated arguments
        /// </summary>
        /// <param name="args">Command line arguments to parse</param>
        /// <exception cref="ArgumentException">Thrown when required parameters are missing or invalid</exception>
        public CommandParameters(string[] args)
        {
            _parameterDefinitions = new List<Parameter>();
            
            if (args.Length < 4)
            {
                throw new ArgumentException(GetUsageString());
            }

            AzureDevOpsUrl = ValidateUrl(args[0], "Azure DevOps URL");
            AzureDevOpsPat = ValidateToken(args[1], "Azure DevOps PAT");
            GitHubOrg = ValidateOrgName(args[2], "GitHub organization");
            GitHubPat = ValidateToken(args[3], "GitHub PAT");

            // Parse optional parameters
            for (int i = 4; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--config":
                        if (i + 1 < args.Length)
                            ConfigPath = args[++i];
                        break;
                    case "--parallel":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int parallel))
                            MaxParallelOperations = Math.Max(1, Math.Min(parallel, 8));
                        break;
                    case "--dry-run":
                        DryRun = true;
                        break;
                }
            }
        }

        /// <summary>
        /// Validates a URL string
        /// </summary>
        /// <param name="url">URL to validate</param>
        /// <param name="paramName">Parameter name for error messages</param>
        /// <returns>The validated URL</returns>
        /// <exception cref="ArgumentException">Thrown when URL is invalid</exception>
        private static string ValidateUrl(string url, string paramName)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                var error = $"{paramName} cannot be empty";
                Logger.LogError(error);
                throw new ArgumentException(error);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                var error = $"{paramName} must be a valid URL: {url}";
                Logger.LogError(error);
                throw new ArgumentException(error);
            }

            return url.TrimEnd('/');
        }

        /// <summary>
        /// Validates an organization name
        /// </summary>
        /// <param name="name">Organization name to validate</param>
        /// <paramName="paramName">Parameter name for error messages</param>
        /// <returns>The validated organization name</returns>
        /// <exception cref="ArgumentException">Thrown when name is invalid</exception>
        private static string ValidateOrgName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                var error = $"{paramName} cannot be empty";
                Logger.LogError(error);
                throw new ArgumentException(error);
            }

            if (name.Contains('/'))
            {
                var error = $"{paramName} should not contain '/': {name}";
                Logger.LogError(error);
                throw new ArgumentException(error);
            }

            return name.Trim();
        }

        /// <summary>
        /// Validates a Personal Access Token
        /// </summary>
        /// <param name="token">Token to validate</param>
        /// <param name="paramName">Parameter name for error messages</param>
        /// <returns>The validated token</returns>
        /// <exception cref="ArgumentException">Thrown when token is invalid</exception>
        private static string ValidateToken(string token, string paramName)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                var error = $"{paramName} cannot be empty";
                Logger.LogError(error);
                throw new ArgumentException(error);
            }

            return token.Trim();
        }

        /// <summary>
        /// Gets the usage string explaining command-line arguments
        /// </summary>
        /// <returns>Formatted usage string</returns>
        private static string GetUsageString()
        {
            return @"Usage: AzureDevOps2GitHubMigrator <AzureDevOpsUrl> <AzureDevOpsPat> <GitHubOrg> <GitHubPat> [options]

Required Arguments:
  AzureDevOpsUrl     URL of your Azure DevOps organization (e.g., https://dev.azure.com/organization)
  AzureDevOpsPat     Personal Access Token for Azure DevOps
  GitHubOrg          Target GitHub organization name
  GitHubPat          Personal Access Token for GitHub

Optional Arguments:
  --config <path>    Path to configuration file
  --parallel <n>     Maximum parallel operations (1-8, default: 4)
  --dry-run         Validate configuration without making changes

Examples:
  AzureDevOps2GitHubMigrator https://dev.azure.com/myorg pat123 destination-org ghp_token123
  AzureDevOps2GitHubMigrator https://dev.azure.com/myorg pat123 destination-org ghp_token123 --parallel 2
  AzureDevOps2GitHubMigrator https://dev.azure.com/myorg pat123 destination-org ghp_token123 --config config.json";
        }
    }
}