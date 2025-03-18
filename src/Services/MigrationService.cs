using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Services;

/// <summary>
/// Core service responsible for orchestrating the migration process from Azure DevOps to GitHub.
/// Handles assessment, validation, and coordination between Azure DevOps and GitHub services.
/// </summary>
public class MigrationService
{
    private readonly AzureDevOpsService _adoService;
    private readonly GitHubService _githubService;

    /// <summary>
    /// Initializes a new instance of the MigrationService
    /// </summary>
    /// <param name="adoService">Service for interacting with Azure DevOps</param>
    /// <param name="githubService">Service for interacting with GitHub</param>
    public MigrationService(AzureDevOpsService adoService, GitHubService githubService)
    {
        _adoService = adoService;
        _githubService = githubService;
    }

    /// <summary>
    /// Runs a pre-migration assessment to evaluate migration readiness and potential issues
    /// </summary>
    /// <param name="projects">Optional comma-separated list of project names to assess. If null, all projects are assessed</param>
    /// <param name="githubOrg">Target GitHub organization name</param>
    /// <param name="repoNamePattern">Pattern for generating GitHub repository names</param>
    /// <param name="teamNamePattern">Pattern for generating GitHub team names</param>
    /// <returns>List of projects with their assessment results and migration status</returns>
    public async Task<List<MigrationProject>> RunAssessmentAsync(string? projects, string githubOrg, string repoNamePattern, string teamNamePattern)
    {
        try
        {
            Logger.LogInfo("Starting pre-migration assessment...\n");
            // Get project names
            var projectNames = string.IsNullOrEmpty(projects) ?
                (await _adoService.GetProjectsAsync()).Select(p => p.Name ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToArray() :
                projects.Replace("\"", "").Replace("\'", "").Split(',');

            // Check GitHub organization access
            Logger.LogInfo($"Checking access to GitHub organization '{githubOrg}'...");
            var hasGitHubAccess = await _githubService.ValidateOrganizationAccessAsync(githubOrg);
            if (!hasGitHubAccess)
            {
                throw new Exception($"Unable to access GitHub organization '{githubOrg}'. Please verify your PAT has admin:org scope.");
            }
            Logger.LogSuccess("✓ GitHub organization access validated\n");

            List<MigrationProject> migrationProjects = new List<MigrationProject>();
            // Assess each project
            foreach (var projectName in projectNames)
            {
                Logger.LogInfo($"Assessing project '{projectName}'...");

                var project = await _adoService.GetProjectAsync(projectName, includeRepos: true, includeTeams: true, includeTeamMembers: true);
                if (project == null || project.Name == null || project.Url == null)
                {
                    Logger.LogWarning($"Project '{projectName}' not found in Azure DevOps organization '{_adoService.DevOpsUrl}'");
                    continue;
                }
                Logger.LogSuccess($"Found project: {Common.CreateClickableLink(project.Name, project.Url)}");
                migrationProjects.Add(ConvertToMigrationProject(project, githubOrg));

                Logger.LogInfo("");
            }

            foreach (var project in migrationProjects)
            {
                foreach (var repo in project.Repos)
                {
                    string githubRepoName;
                    if (string.IsNullOrWhiteSpace(repoNamePattern))
                    {
                        githubRepoName = (repo.Name == project.Name) ? repo.Name ?? string.Empty : $"{project.Name}-{repo.Name}";
                    }
                    else
                    {
                        githubRepoName = repoNamePattern
                            .Replace("{projectName}", project.Name ?? string.Empty)
                            .Replace("{repoName}", repo.Name ?? string.Empty);
                        // Normalize the name to avoid duplicates due Azure DevOps default behavior
                        // where the repo name is the same as the project name
                        if (githubRepoName.Contains($"{project.Name}-{project.Name}") )
                        {
                            githubRepoName = githubRepoName.Replace($"{project.Name}-{project.Name}", project.Name ?? string.Empty);
                        }
                       
                    }
                     githubRepoName = Common.NormalizeName(githubRepoName);
                     
                    if (string.IsNullOrWhiteSpace(githubRepoName))
                    {
                        Logger.LogWarning($"Unable to generate GitHub repository name for '{repo.Name}'");
                        continue;
                    }

                    repo.GitHubRepoName = githubRepoName;
                    if (await _githubService.RepositoryExistsAsync(githubOrg, githubRepoName))
                    {
                        Logger.LogWarning($"Repository '{githubRepoName}' already exists in GitHub organization '{githubOrg}'.");
                        repo.GitHubRepoMigrationStatus = MigrationStatus.PartiallyCompleted;
                        if (!await _githubService.IsRepositoryEmptyAsync(githubOrg, githubRepoName))
                        {
                            repo.GitHubRepoMigrationStatus = MigrationStatus.Completed;
                        }
                    }
                    else
                    {
                        Logger.LogInfo($"Repository '{repo.Name}' will be migrated as '{githubRepoName}'");
                    }
                }

                foreach (var team in project.Teams)
                {
                    string githubTeamName;
                    if (string.IsNullOrWhiteSpace(teamNamePattern))
                    {
                        githubTeamName = team.Name ?? string.Empty;
                    }
                    else
                    {
                        githubTeamName = teamNamePattern
                            .Replace("{projectName}", project.Name ?? string.Empty)
                            .Replace("{teamName}", team.Name ?? string.Empty);
                    }
                    githubTeamName = Common.NormalizeName(githubTeamName);

                    if (string.IsNullOrWhiteSpace(githubTeamName))
                    {
                        Logger.LogWarning($"Unable to generate GitHub team name for '{team.Name}'");
                        continue;
                    }

                    team.GitHubTeamName = githubTeamName;
                    if (await _githubService.TeamExistsAsync(githubOrg, githubTeamName))
                    {
                        Logger.LogWarning($"Team '{githubTeamName}' already exists in GitHub organization '{githubOrg}'.");
                        team.GitHubTeamMigrationStatus = MigrationStatus.PartiallyCompleted;
                    }
                    else
                    {
                        if (team.Members.Count == 0)
                        {
                            team.GitHubTeamMigrationStatus = MigrationStatus.Skipped;
                        }
                        else
                        {
                            team.GitHubTeamMigrationStatus = MigrationStatus.Pending;
                        }
                    }
                    Logger.LogInfo($"Team '{team.Name}' will be migrated as '{githubTeamName}'");
                }
            }

            Logger.LogSuccess("Assessment completed successfully.");
            return migrationProjects;

        }
        catch (Exception ex)
        {
            Logger.LogError("Assessment failed", ex);
            throw new Exception("Assessment failed", ex);
        }
    }

    /// <summary>
    /// Converts an Azure DevOps project to a migration project model
    /// </summary>
    /// <param name="project">Source Azure DevOps project</param>
    /// <param name="githubOrg">Target GitHub organization</param>
    /// <returns>Migration project with initialized repositories and teams</returns>
    private MigrationProject ConvertToMigrationProject(AdoProject project, string githubOrg)
    {
        var migrationProject = new MigrationProject
        {
            Id = project.Id,
            Name = project.Name,
            Url = project.Url,
            State = project.State,
            Visibility = project.Visibility,
            AdoOrganization = project.AdoOrganization,
            GitHubOrganization = githubOrg,

            Repos = project.Repos.Select(r => new MigrationRepository
            {
                Id = r.Id,
                Name = r.Name,
                ProjectName = project.Name,
                Url = r.Url,
                Size = r.Size,
                DefaultBranch = r.DefaultBranch,
                GitHubRepoMigrationStatus = MigrationStatus.Pending
            }).ToList(),
            Teams = project.Teams.Select(t => new MigrationTeam
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                ProjectName = project.Name,
                Members = t.Members?.Select(m => new MigrationTeamMember
                {
                    UniqueName = m.Identity.UniqueName,
                    DisplayName = m.Identity.DisplayName
                }).ToList() ?? new List<MigrationTeamMember>(),
                GitHubTeamMigrationStatus = MigrationStatus.Pending
            }).ToList(),
            ProjectMigrationStatus = MigrationStatus.Pending
        };

        return migrationProject;
    }

 
}