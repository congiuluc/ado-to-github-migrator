using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Services;

/// <summary>
/// Service responsible for assessing Azure DevOps projects and their components for migration
/// </summary>
public class AssessmentService
{
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly string _baseUrl;
    private readonly string _adoOrg;
    private readonly string _adoPat;

    /// <summary>
    /// Initializes a new instance of the AssessmentService
    /// </summary>
    /// <param name="httpClient">The HTTP client used for making requests</param>
    /// <param name="adoOrg">The Azure DevOps organization name</param>
    /// <param name="adoPat">Personal Access Token for Azure DevOps authentication</param>
    /// <param name="baseUrl">Base URL for Azure DevOps API (defaults to https://dev.azure.com)</param>
    /// <param name="adoVersion">Version of Azure DevOps (defaults to 'cloud')</param>
    public AssessmentService(HttpClient httpClient, string adoOrg, string adoPat, string baseUrl = "https://dev.azure.com", string adoVersion="cloud")
    {
        _baseUrl = $"{baseUrl}/{adoOrg}";
        _adoOrg = adoOrg;
        _adoPat = adoPat;
        _azureDevOpsService = new AzureDevOpsService(httpClient, _baseUrl, adoPat, adoVersion);
    }

    /// <summary>
    /// Assesses Azure DevOps projects and their components for migration
    /// </summary>
    /// <param name="projectNames">Optional array of specific project names to assess. If null, all projects will be assessed</param>
    /// <returns>A list of MigrationProject objects containing assessment results</returns>
    /// <remarks>
    /// This method performs the following assessments:
    /// - Projects and their basic information
    /// - Git and TFVC repositories within each project
    /// - Teams and their members
    /// All components are prepared for migration with a Pending status
    /// </remarks>
    public async Task<List<MigrationProject>> AssessAsync(string[]? projectNames = null)
    {
        // Start assessment
        Logger.LogInfo($"Azure DevOps organization: {_adoOrg}");
        Logger.LogInfo($"Base URL: { _baseUrl}");
        Logger.LogInfo($"Azure DevOps PAT: {new string('*', _adoPat.Length)}");

        Logger.LogInfo($"Starting Azure DevOps assessment using {_baseUrl}...");

        // Get projects
        var adoProjects = new List<AdoProject>();
        if (projectNames != null)
        {
            foreach (var name in projectNames)
            {
                try
                {
                    var proj = await _azureDevOpsService.GetProjectAsync(name, includeRepos: true, includeTeams:true );
                    if (proj != null)
                    {
                        adoProjects.Add(proj);
                        Logger.LogSuccess($"Found project: {proj.Url}");
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
                    Logger.LogSuccess($"Found project: {proj.Url}");
                }
            }
            adoProjects.AddRange(allProjects);
        }

        if (!adoProjects.Any())
        {
            Logger.LogWarning("No projects found to assess.");
            return new List<MigrationProject>();
        }

        Logger.LogInfo($"Found {adoProjects.Count} projects to assess");
        var repositories = new List<Repository>();
        var teams = new List<AdoTeam>();

        var migrationProjects = new List<MigrationProject>();
        foreach (var adoProject in adoProjects)
        {
            if (adoProject.Name != null)
            {
                Logger.LogInfo($"\nAssessing project: {adoProject.Name}");

                // Get repositories
                var projectRepos = await _azureDevOpsService.GetRepositoriesAsync(adoProject.Name);
                if (projectRepos?.Any() == true)
                {
                    Logger.LogSuccess($"Found {projectRepos.Count} repositories:");
                    foreach (var repo in projectRepos)
                    {
                        if (repo.Name != null)
                        {
                            Logger.LogInfo($"- {repo.Name} ({repo.RepositoryType.ToUpper()})");
                        }
                    }
                    repositories.AddRange(projectRepos);
                    adoProject.Repos = projectRepos;
                }
                
                // Get teams and members
                var projectTeams = await _azureDevOpsService.GetTeamsAsync(adoProject.Name, true);
                if (projectTeams?.Any() == true)
                {
                    Logger.LogSuccess($"Found {projectTeams.Count} teams:");
                    teams.AddRange(projectTeams);
                    adoProject.Teams = projectTeams;
                }

                // Convert to MigrationProject
                var migrationProject = new MigrationProject
                {
                    Id = adoProject.Id,
                    Name = adoProject.Name,
                    Url = adoProject.Url,
                    State = adoProject.State,
                    Visibility = adoProject.Visibility,
                    AdoOrganization = adoProject.AdoOrganization,
                    GitHubOrganization = null, // Will be set during migration
                    ProjectMigrationStatus = MigrationStatus.Pending
                };

                // Convert repositories
                migrationProject.Repos = adoProject.Repos.Select(r => new MigrationRepository
                {
                    Id = r.Id,
                    Name = r.Name,
                    ProjectName = adoProject.Name,
                    Url = r.Url,
                    Size = r.Size,
                    DefaultBranch = r.DefaultBranch,
                    GitHubRepoMigrationStatus = MigrationStatus.Pending
                }).ToList();

                // Convert teams
                migrationProject.Teams = adoProject.Teams.Select(t => new MigrationTeam
                {
                    Id = t.Id,
                    Name = t.Name,
                    Description = t.Description,
                    ProjectName = adoProject.Name,
                    Members = t.Members?
                        .Where(m => m?.Identity != null)
                        .Select(m => new MigrationTeamMember
                        {
                            UniqueName = m.Identity?.UniqueName ?? string.Empty,
                            DisplayName = m.Identity?.DisplayName ?? string.Empty,
                            GitHubUserMigrationStatus = MigrationStatus.Pending
                        })
                        .ToList() ?? new List<MigrationTeamMember>(),
                    GitHubTeamMigrationStatus = MigrationStatus.Pending
                }).ToList();

                migrationProjects.Add(migrationProject);
            }
        }

        return migrationProjects;
    }
}