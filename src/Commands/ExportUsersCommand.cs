using System.CommandLine;
using AzureDevOps2GitHubMigrator.Services;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Models.Ado;
using AzureDevOps2GitHubMigrator.Models.GitHub;
using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.Utils;
using AzureDevOps2GitHubMigrator.GitHub;
using Microsoft.Extensions.Configuration;
using System.CommandLine.Invocation;

namespace AzureDevOps2GitHubMigrator.Commands;

public class ExportUsersCommand
{
    public static Command Create()
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

        // Add an optional configuration file path option
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

            // Ensure working directory exists
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
            adoVersion = adoVersion ?? config["AzureDevOps:Version"] ?? "cloud";
            adoBaseUrl = adoBaseUrl ?? config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com";

           
            var adoUrl = $"{adoBaseUrl}/{finalAdoOrg}";

            using var httpClient = new HttpClient();
            var adoService = new AzureDevOpsService(httpClient, adoUrl, finalAdoPat!, adoVersion);

            try
            {
                Logger.LogInfo($"Extracting users from Azure DevOps organization: {finalAdoOrg}");

                List<AdoTeamMember> users;
                if (!string.IsNullOrEmpty(adoProjects))
                {
                    var projectNames = adoProjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    users = new List<AdoTeamMember>();
                    foreach (var projectName in projectNames)
                    {
                        var projectTeams = await adoService.GetTeamsAsync(projectName);
                        foreach (var team in projectTeams)
                        {
                            if (!string.IsNullOrEmpty(team.Id))
                            {
                                var teamMembers = await adoService.GetTeamMembersAsync(projectName, team.Id);
                                users.AddRange(teamMembers);
                            }
                        }
                    }
                    users = users.DistinctBy(u => u.Identity?.Id).ToList();
                }
                else
                {
                    users = new List<AdoTeamMember>();
                    var allProjects = await adoService.GetProjectsAsync();
                    foreach (var project in allProjects)
                    {
                        if (!string.IsNullOrEmpty(project.Name))
                        {
                            var projectTeams = await adoService.GetTeamsAsync(project.Name);
                            foreach (var team in projectTeams)
                            {
                                if (!string.IsNullOrEmpty(team.Id))
                                {
                                    var teamMembers = await adoService.GetTeamMembersAsync(project.Name, team.Id);
                                    users.AddRange(teamMembers);
                                }
                            }
                        }
                    }
                    users = users.DistinctBy(u => u.Identity?.Id).ToList();
                }

                if (!includeInactive)
                {
                    users = users.Where(u => u.Identity?.IsEnabled ?? true).ToList();
                }

                if (!users.Any())
                {
                    Logger.LogWarning("No users found to export.");
                    return;
                }

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



    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}