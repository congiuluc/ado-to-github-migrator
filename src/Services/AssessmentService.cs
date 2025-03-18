using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Services;

public class AssessmentService
{
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly string _baseUrl;
    private readonly string _adoOrg;
    private readonly string _adoPat;

    public AssessmentService(HttpClient httpClient, string adoOrg, string adoPat, string baseUrl = "https://dev.azure.com", string apiVersion="7.1")
    {
        _baseUrl = $"{baseUrl}/{adoOrg}";
        _adoOrg = adoOrg;
        _adoPat = adoPat;
        _azureDevOpsService = new AzureDevOpsService(httpClient, _baseUrl, adoPat, apiVersion);
    }

    public async Task<List<AdoProject>> AssessAsync(string[]? projectNames = null)
    {
        // Start assessment
        Logger.LogInfo($"Azure DevOps organization: {_adoOrg}");
        Logger.LogInfo($"Base URL: {Common.CreateClickableLink(_baseUrl, _baseUrl)}");
        Logger.LogInfo($"Azure DevOps PAT: {new string('*', _adoPat.Length)}");

        Logger.LogInfo($"Starting Azure DevOps assessment using {Common.CreateClickableLink(_baseUrl, _baseUrl)}...");

        // Get projects
        var projects = new List<AdoProject>();
        if (projectNames != null)
        {
            foreach (var name in projectNames)
            {
                try
                {
                    var proj = await _azureDevOpsService.GetProjectAsync(name);
                    if (proj != null)
                    {
                        projects.Add(proj);
                        Logger.LogSuccess($"Found project: {Common.CreateClickableLink(name, proj.Url)}");
                    }
                    else
                        Logger.LogWarning($"Project '{name}' not found");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to get project '{name}': {ex.Message}", ex);
                }
            }
        }
        else
        {
            var allProjects = await _azureDevOpsService.GetProjectsAsync();
            foreach (var proj in allProjects)
            {
                if (proj.Name != null)
                {
                    Logger.LogSuccess($"Found project: {Common.CreateClickableLink(proj.Name, proj.Url)}");
                }
            }
            projects.AddRange(allProjects);
        }

        if (!projects.Any())
        {
            Logger.LogWarning("No projects found to assess.");
            return null!;
        }

        Logger.LogInfo($"Found {projects.Count} projects to assess");
        var repositories = new List<Repository>();
        var teams = new List<AdoTeam>();

        foreach (var project in projects)
        {
            if (project.Name != null)
            {
                Logger.LogInfo($"\nAssessing project: {Common.CreateClickableLink(project.Name, project.Url)}");

                // Get repositories
                var projectRepos = await _azureDevOpsService.GetRepositoriesAsync(project.Name);
                if (projectRepos?.Any() == true)
                {
                    Logger.LogSuccess($"Found {projectRepos.Count} repositories:");
                    foreach (var repo in projectRepos)
                    {
                        if (repo.Name != null)
                        {
                            Logger.LogInfo($"- {Common.CreateClickableLink(repo.Name, repo.Url)} ({repo.RepositoryType.ToUpper()})");
                        }
                    }
                    repositories.AddRange(projectRepos);
                    project.Repos = projectRepos;
                }
                
                // Get teams and members
                var projectTeams = await _azureDevOpsService.GetTeamsAsync(project.Name, true);
                if (projectTeams?.Any() == true)
                {
                    Logger.LogSuccess($"Found {projectTeams.Count} teams:");
                    teams.AddRange(projectTeams);
                    project.Teams = projectTeams;
                }
            }
        }

        return projects;
    }
}