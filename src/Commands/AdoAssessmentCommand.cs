using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class AdoAssessmentCommand
{
    public static Command Create()
    {
        var command = new Command("ado-assessment", "Assess Azure DevOps repositories for migration");

        // Required options but can be provided via config
        var adoOrgOption = new Option<string>("--ado-org", "Azure DevOps organization name");
        var adoPatOption = new Option<string>("--ado-pat", "Azure DevOps Personal Access Token");

        // Optional options with defaults
        var adoVersionOption = new Option<string>("--ado-version",  "Azure DevOps version (e.g., 2019, 2020, 2022, cloud)");
        var adoBaseUrlOption = new Option<string>("--ado-baseurl", "Azure DevOps Server base URL");
        var adoProjectsOption = new Option<string>("--ado-projects", "Comma-separated list of project names");
        var outputFormatOption = new Option<string>("--output", "Output format: json or md");
        var workingDirOption = new Option<string>("--working-dir", "Working directory for output files");
        var configFilePathOption = new Option<string?>("--config", "Path to the configuration file");

        command.AddOption(adoOrgOption);
        command.AddOption(adoPatOption);
        command.AddOption(adoVersionOption);
        command.AddOption(adoBaseUrlOption);
        command.AddOption(adoProjectsOption);
        command.AddOption(outputFormatOption);
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
            var outputFormat = context.ParseResult.GetValueForOption(outputFormatOption) ?? "json";

            // Validate required values
            var missingParameters = new List<string>();
            if (string.IsNullOrEmpty(finalAdoOrg))
                missingParameters.Add("--ado-org (Azure DevOps organization)");
            if (string.IsNullOrEmpty(finalAdoPat))
                missingParameters.Add("--ado-pat (Azure DevOps PAT)");

            if (missingParameters.Any())
            {
                Logger.LogError($"The following required parameters are missing: {string.Join(", ", missingParameters)}");
                Logger.LogError("Please provide them via command-line options or in the configuration file.");
                return; // Gracefully exit
            }

            var httpClient = new HttpClient();
            // finalAdoOrg and finalAdoPat are checked for null above, so null-forgiving operator is safe here
            var assessmentService = new AssessmentService(httpClient, finalAdoOrg!, finalAdoPat!, adoBaseUrl, adoVersion);
            
            try
            {
                Logger.LogInfo("Running assessment...");
                var projectNames = string.IsNullOrEmpty(adoProjects) ? null : adoProjects.Split(',');
                var results = await assessmentService.AssessAsync(projectNames);

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                var outputFile = $"{timestamp}_{finalAdoOrg}_assessment.{outputFormat}";

                if (outputFormat.Equals("md", StringComparison.OrdinalIgnoreCase))
                {
                    var report = new MigrationReport(isMarkdown: true);
                    foreach (var project in results)
                    {
                        report.AddProject(project);
                    }
                    report.AddStatistics();
                    await report.SaveAsync(1); // Using 1 as default worker count for assessment
                }
                else
                {
                    await File.WriteAllTextAsync(outputFile, results.ToJson());
                }

                Logger.LogSuccess($"Assessment completed successfully. Results saved to {workingDir}\\{outputFile}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Assessment failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}