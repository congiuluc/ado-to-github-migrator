using AzureDevOps2GitHubMigrator.AzureDevOps;
using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Services;

/// <summary>
/// Represents the main service for orchestrating the migration process from Azure DevOps to GitHub.
/// </summary>
public class MigrationService
{
    private readonly AzureDevOpsService _adoService;
    private readonly GitHubService _githubService;
    private readonly string _adoPat;
    private readonly string _githubPat;

    /// <summary>
    /// Initializes a new instance of the MigrationService class.
    /// </summary>
    /// <param name="adoService">The Azure DevOps service instance.</param>
    /// <param name="githubService">The GitHub service instance.</param>
    public MigrationService(AzureDevOpsService adoService, GitHubService githubService)
    {
        _adoService = adoService;
        _githubService = githubService;
        _adoPat = adoService.GetPat();
        _githubPat = githubService.GetPat();
    }

    /// <summary>
    /// Runs a pre-migration assessment to evaluate migration readiness and potential issues.
    /// </summary>
    /// <param name="adoOrg">The Azure DevOps organization name.</param>
    /// <param name="projects">Optional comma-separated list of project names to assess. If null, all projects are assessed.</param>
    /// <param name="githubOrg">The target GitHub organization name.</param>
    /// <param name="repoNamePattern">The pattern for generating GitHub repository names.</param>
    /// <param name="teamNamePattern">The pattern for generating GitHub team names.</param>
    /// <param name="usersMappingFile">The path to the users mapping file.</param>
    /// <returns>A list of projects with their assessment results and migration status.</returns>
    public async Task<List<MigrationProject>> RunAssessmentAsync(string? adoOrg, string? projects, string githubOrg, string repoNamePattern, string teamNamePattern, string usersMappingFile)
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
                Logger.LogSuccess($"Found project: {project.Name}");
                migrationProjects.Add(ConvertToMigrationProject(project, githubOrg));

                Logger.LogInfo("");
            }

            var usersMapping = new List<MappingUser>();
            if (!string.IsNullOrEmpty(usersMappingFile) && File.Exists(usersMappingFile))
            {
                Logger.LogInfo("Loading users mapping file...");
                usersMapping = Common.LoadUsersMapping(usersMappingFile);
                Logger.LogSuccess($"Loaded {usersMapping.Count} users mapping from file: {usersMappingFile}");
            }

            foreach (var project in migrationProjects)
            {
                foreach (var repo in project.Repos)
                {
                    string githubRepoName = Common.CreateRepositoryName(adoOrg, project.Name, repo.Name, repoNamePattern);

                    if (string.IsNullOrWhiteSpace(githubRepoName))
                    {
                        Logger.LogWarning($"Unable to generate GitHub repository name for '{repo.Name}'");
                        continue;
                    }

                    repo.GitHubRepoName = githubRepoName;

                    if (await _githubService.RepositoryExistsAsync(githubOrg, githubRepoName))
                    {
                        repo.GitHubRepoUrl = $"https://github.com/{githubOrg}/{githubRepoName}";
                        Logger.LogWarning($"Repository '{githubRepoName}' already exists in GitHub organization '{githubOrg}'.");
                        repo.GitHubRepoMigrationStatus = MigrationStatus.PartiallyCompleted;
                        if (!await _githubService.IsRepositoryEmptyAsync(githubOrg, githubRepoName))
                        {

                            var defaultBranch = await _githubService.GetDefaultBranchAsync(githubOrg, githubRepoName);
                            if (!string.Equals(defaultBranch, repo.DefaultBranch, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.LogWarning($"Repository '{githubRepoName}' has a different default branch '{defaultBranch}' than expected '{repo.DefaultBranch}'.");
                                repo.GitHubRepoMigrationStatus = MigrationStatus.PartiallyCompleted;
                                repo.GitHubRepoMigrationError = $"Repository already exists with different default branch '{defaultBranch}'";
                            }
                            else
                            {
                                Logger.LogInfo($"Repository '{githubRepoName}' already exists with the same default branch '{repo.DefaultBranch}'");
                                repo.GitHubRepoMigrationStatus = MigrationStatus.Completed;
                            }


                        }
                    }
                    else if (repo.RepositoryType.Equals("git", StringComparison.OrdinalIgnoreCase) && repo.BranchCount == 0)
                    {
                        repo.GitHubRepoMigrationStatus = MigrationStatus.Skipped;
                        repo.GitHubRepoMigrationError = "No branches to migrate";
                        Logger.LogWarning($"Repository '{repo.Name}' has no branches to migrate. Skipping migration.");
                    }
                    else
                    {
                        Logger.LogInfo($"Repository '{repo.Name}' will be migrated as '{githubRepoName}'");
                    }
                }

                foreach (var team in project.Teams)
                {
                    string githubTeamName = Common.CreateTeamName(adoOrg, project.Name, team.Name, teamNamePattern);
                    if (string.IsNullOrWhiteSpace(githubTeamName))
                    {
                        Logger.LogWarning($"Unable to generate GitHub team name for '{team.Name}'");
                        continue;
                    }

                    team.GitHubTeamName = githubTeamName;
                    var teamExists = await _githubService.TeamExistsAsync(githubOrg, githubTeamName);
                    if (teamExists)
                    {
                        Logger.LogWarning($"Team '{githubTeamName}' already exists in GitHub organization '{githubOrg}'.");
                        team.GitHubTeamMigrationStatus = MigrationStatus.PartiallyCompleted;
                        team.GitHubTeamUrl = $"https://github.com/orgs/{githubOrg}/teams/{githubTeamName}";
                        foreach (var member in team.Members)
                        {
                            var userMapping = usersMapping.FirstOrDefault(u => u.AdoUser == member.UniqueName);
                            if (userMapping != null)
                            {
                                member.GitHubUserName = userMapping.GitHubUser;
                                if (userMapping.GitHubUser != null)
                                {
                                    var membershipState = await _githubService.GetTeamMembershipStateAsync(githubOrg, githubTeamName, userMapping.GitHubUser);

                                    if (membershipState == "active")
                                    {
                                        member.GitHubUserMigrationStatus = MigrationStatus.Completed;
                                    }
                                    else
                                    {
                                        member.GitHubUserMigrationStatus = MigrationStatus.Pending;
                                    }
                                }


                            }
                            else
                            {
                                member.GitHubUserMigrationStatus = MigrationStatus.Failed;
                                member.GitHubUserMigrationError = "User not found in mapping file";
                            }

                        }
                        if (team.Members.All(m => m.GitHubUserMigrationStatus == MigrationStatus.Completed))
                        {
                            team.GitHubTeamMigrationStatus = MigrationStatus.Completed;
                        }
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
                            foreach (var member in team.Members)
                            {
                                var userMapping = usersMapping.FirstOrDefault(u => u.AdoUser == member.UniqueName);
                                if (userMapping != null)
                                {
                                    member.GitHubUserName = userMapping.GitHubUser;
                                }
                            }
                        }
                        Logger.LogInfo($"Team '{team.Name}' will be migrated as '{githubTeamName}'");

                    }

                }
            }

            // Update project status after assessment
            foreach (var project in migrationProjects)
            {
                UpdateProjectStatus(project);
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
    /// Converts an Azure DevOps project to a migration project model.
    /// </summary>
    /// <param name="project">The source Azure DevOps project.</param>
    /// <param name="githubOrg">The target GitHub organization.</param>
    /// <returns>A migration project with initialized repositories and teams.</returns>
    private MigrationProject ConvertToMigrationProject(AdoProject project, string githubOrg)
    {
        var migrationProject = new MigrationProject
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
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
                RepositoryType = r.RepositoryType,
                DefaultBranch = r.DefaultBranch,
                GitHubRepoMigrationStatus = MigrationStatus.Pending,
                Visibility = project.Visibility,
                BranchCount = r.Branches?.Count ?? 0
            }).ToList(),
            Teams = project.Teams.Select(t => new MigrationTeam
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                ProjectName = project.Name,
                Members = t.Members?
                    .Where(m => m?.Identity != null)
                    .Select(m => new MigrationTeamMember
                    {
                        UniqueName = m.Identity?.UniqueName ?? string.Empty,
                        DisplayName = m.Identity?.DisplayName ?? string.Empty,
                        IsTeamAdmin = m.IsTeamAdmin,
                        Id = m.Identity?.Id,
                        IsGroup = m.Identity?.IsContainer ?? false

                    })
                    .ToList() ?? new List<MigrationTeamMember>(),
                GitHubTeamMigrationStatus = MigrationStatus.Pending
            }).ToList(),
            ProjectMigrationStatus = MigrationStatus.Pending
        };

        return migrationProject;
    }

    /// <summary>
    /// Performs the actual migration of repositories and teams from Azure DevOps to GitHub.
    /// </summary>
    /// <param name="projects">The list of projects to migrate.</param>
    /// <param name="migrateTeams">Indicates whether to migrate teams.</param>
    /// <param name="workingDir">The working directory for migration operations.</param>
    /// <param name="gitDisableSslVerify">Indicates whether to disable SSL verification for Git operations.</param>
    /// <param name="gitUsePatForClone">Indicates whether to use PAT for cloning repositories.</param>
    public async Task MigrateAsync(List<MigrationProject> projects, bool migrateTeams, string workingDir, bool gitDisableSslVerify = false, bool gitUsePatForClone = false)
    {
        try
        {
            List<MappingUser> usersMapping = new List<MappingUser>();

            foreach (var project in projects.Where(p => p.Name != null))
            {
                project.ProjectMigrationStatus = MigrationStatus.InProgress;
                var repos = project.Repos.Where(r =>
                    r.GitHubRepoMigrationStatus != MigrationStatus.Completed &&
                    r.GitHubRepoMigrationStatus != MigrationStatus.Skipped).ToList();

                foreach (var repo in repos)
                {
                    if (repo.Name == null || repo.GitHubRepoName == null || repo.Url == null)
                    {
                        Logger.LogWarning($"Skipping repository with missing required information in project: {project.Name}");
                        continue;
                    }

                    Logger.LogInfo($"Migrating repository: {repo.Name} from project: {project.Name}");
                    var repoExists = await _githubService.RepositoryExistsAsync(project.GitHubOrganization!, repo.GitHubRepoName);

                    if (!repoExists)
                    {
                        Logger.LogInfo($"Repository {repo.GitHubRepoName} does not exist in GitHub. Creating it...");
                        var description = string.IsNullOrWhiteSpace(project.Description) ? $"Migrated from {project.Url!}" : $"{project.Description} (Migrated from {project.Url!})";
                        var repoCreated = await _githubService.CreateRepositoryAsync(project.GitHubOrganization!, repo.GitHubRepoName, description, true);
                        if (repoCreated?.Id == null)
                        {
                            Logger.LogError($"Failed to create repository {repo.GitHubRepoName} in GitHub");
                            repo.GitHubRepoMigrationStatus = MigrationStatus.Failed;
                            continue;
                        }
                        repo.GitHubRepoMigrationStatus = MigrationStatus.PartiallyCompleted;
                        Logger.LogSuccess($"Repository {repo.GitHubRepoName} created successfully in GitHub.");
                    }

                    var repoMigrated = await RepositoryMigrator.MigrateRepositoryContentAsync(
                        repo.Url,
                        project.GitHubOrganization!,
                        _githubService.GetPat(),
                        project.Name!,
                        repo.GitHubRepoName,
                        repo.DefaultBranch!,
                        _adoService.DevOpsBaseUrl(),
                        _adoService.GetPat(),
                        workingDir,
                        repo.RepositoryType.Equals("tfvc", StringComparison.OrdinalIgnoreCase),
                        gitDisableSslVerify: gitDisableSslVerify,
                        gitUsePatForClone: gitUsePatForClone
                    );
                    repo.GitHubRepoMigrationStatus = repoMigrated;
                    switch (repoMigrated)
                    {
                        case MigrationStatus.Completed:
                            Logger.LogSuccess($"Successfully migrated repository: {repo.Name}");
                            break;
                        case MigrationStatus.PartiallyCompleted:
                            Logger.LogWarning($"Partially migrated repository: {repo.Name} - some commits may not match");
                            break;
                        case MigrationStatus.Skipped:
                            Logger.LogInfo($"Repository migration skipped: {repo.Name}");
                            break;
                        default:
                            Logger.LogError($"Failed to migrate repository: {repo.Name}");
                            break;
                    }
                }

                if (migrateTeams && project.Teams.Any())
                {
                    Logger.LogInfo($"\nMigrating teams for project: {project.Name}...");
                    foreach (var team in project.Teams.Where(t =>
                        t.GitHubTeamMigrationStatus != MigrationStatus.Completed &&
                        t.GitHubTeamMigrationStatus != MigrationStatus.Skipped))
                    {
                        if (team.Name == null || team.GitHubTeamName == null)
                        {
                            Logger.LogWarning($"Skipping team with missing required information in project: {project.Name}");
                            continue;
                        }

                        Logger.LogInfo($"Migrating team: {team.Name}");
                        var teamExists = await _githubService.TeamExistsAsync(project.GitHubOrganization!, team.GitHubTeamName);
                        if (!teamExists)
                        {
                            try
                            {
                                await _githubService.CreateTeamAsync(project.GitHubOrganization!, team.GitHubTeamName);
                                team.GitHubTeamMigrationStatus = MigrationStatus.PartiallyCompleted;
                                Logger.LogSuccess($"Team {team.GitHubTeamName} created successfully in GitHub.");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"Failed to create team {team.GitHubTeamName} in GitHub: {ex.Message}");
                                team.GitHubTeamMigrationStatus = MigrationStatus.Failed;
                                team.GitHubTeamMigrationError = ex.Message;
                                continue;
                            }
                        }

                        if (team.Members?.Any() == true)
                        {
                            var migrationResult = await _githubService.MigrateTeamMembersAsync(
                                project.GitHubOrganization!,
                                team.GitHubTeamName,
                                team.Members);


                            foreach (var member in team.Members)
                            {
                                var migrated = migrationResult.Members.FirstOrDefault(m => m.GitHubUsername == member.GitHubUserName);
                                if (migrated == null)
                                {
                                    Logger.LogWarning($"Failed to migrate member {member.GitHubUserName} to team {team.GitHubTeamName}");
                                    member.GitHubUserMigrationStatus = MigrationStatus.Failed;
                                    member.GitHubUserMigrationError = "Member not found in migration result";
                                    continue;
                                }
                                member.GitHubUserMigrationStatus = migrated!.Status;
                                // Check if the user is an Azure DevOps team administrator
                                if (member.IsTeamAdmin)
                                {
                                    if (member.GitHubUserName != null)
                                    {
                                        Logger.LogInfo($"Set user '{member.GitHubUserName}' as in GitHub team '{team.GitHubTeamName}' admin.");
                                        var teamAdmin = await _githubService.SetTeamAdminAsync(project.GitHubOrganization!, team.GitHubTeamName, member.GitHubUserName);
                                        if (!teamAdmin)
                                        {
                                            member.GitHubUserMigrationStatus = MigrationStatus.PartiallyCompleted;
                                            member.GitHubUserMigrationError = $"Member not team {team.GitHubTeamName} admin";
                                        }

                                    }
                                }
                            }

                        }
                        else
                        {
                            team.GitHubTeamMigrationStatus = MigrationStatus.Completed;
                            Logger.LogInfo($"No members to migrate for team {team.Name}");
                        }
                        // Grant repository access to all teams in the organization

                        Logger.LogInfo($"Setting up team access for repository");
                        foreach (var repo in project.Repos.Where(r => r.GitHubRepoName != null && (r.GitHubRepoMigrationStatus == MigrationStatus.Completed || r.GitHubRepoMigrationStatus == MigrationStatus.PartiallyCompleted)))
                        {

                            Logger.LogInfo($"Setting up team access for repository: {repo.GitHubRepoName}");
                            try
                            {
                                var teamAccess = await _githubService.SetTeamRepositoryPermissionAsync(
                                    project.GitHubOrganization!,
                                    team.GitHubTeamName,
                                    repo.GitHubRepoName!,
                                    team.GitHubTeamRole!

                                );
                                if (teamAccess)
                                {
                                    Logger.LogSuccess($"Team {team.GitHubTeamName} granted access to repository {repo.GitHubRepoName}");
                                }
                                else
                                {
                                    Logger.LogWarning($"Failed to set access for team {team.GitHubTeamName} to repository {repo.GitHubRepoName}");
                                    team.GitHubTeamMigrationStatus = MigrationStatus.PartiallyCompleted;
                                }

                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"Failed to set repository access for team {team.GitHubTeamName}: {ex.Message}");
                            }
                        }


                    }


                }

                // Update project status after processing each project
                UpdateProjectStatus(project);
            }

            // After repository and member migration, check and update the default branch if necessary
            Logger.LogInfo("Checking and updating default branches for migrated repositories...");
            Thread.Sleep(5000);
            foreach (var project in projects)
            {
                foreach (var repo in project.Repos.Where(r => r.GitHubRepoMigrationStatus == MigrationStatus.Completed && r.RepositoryType.ToLower() == "git"))
                {

                    try
                    {
                        Logger.LogInfo($"Checking default branch for repository: {repo.GitHubRepoName}");
                        var currentDefaultBranch = await _githubService.GetDefaultBranchAsync(project.GitHubOrganization!, repo.GitHubRepoName!);

                        if (!string.Equals(currentDefaultBranch, repo.DefaultBranch, StringComparison.OrdinalIgnoreCase))
                        {
                            // Adding a do-while loop to retry the default branch update up to 3 times with a 5-second delay between attempts
                            int retryCount = 0;
                            const int maxRetries = 3;
                            do
                            {
                                try
                                {
                                    Logger.LogInfo($"Updating default branch for repository: {repo.GitHubRepoName} from '{currentDefaultBranch}' to '{repo.DefaultBranch}'");

                                    // Use GitHub CLI to update the default branch
                                    var (success, output, error) = await ProcessRunner.RunProcessAsync(
                                        "gh",
                                        $"api repos/{project.GitHubOrganization}/{repo.GitHubRepoName} --method PATCH -f default_branch={repo.DefaultBranch} -H \"Authorization: token {_githubPat}\"",
                                        workingDirectory: workingDir,
                                        timeoutSeconds: 60);

                                    if (!success)
                                    {
                                        throw new Exception($"Failed to update default branch: {error}");
                                    }

                                    Logger.LogSuccess($"Default branch updated successfully for repository: {repo.GitHubRepoName}");
                                    break; // Exit loop if successful
                                }
                                catch (Exception ex)
                                {
                                    retryCount++;
                                    if (retryCount >= maxRetries)
                                    {
                                        Logger.LogError($"Failed to update default branch for repository: {repo.GitHubRepoName} after {maxRetries} attempts", ex);
                                        throw;
                                    }
                                    Logger.LogWarning($"Retrying to update default branch for repository: {repo.GitHubRepoName} (Attempt {retryCount} of {maxRetries})");
                                    await Task.Delay(5000); // Wait for 5 seconds before retrying
                                }
                            } while (retryCount < maxRetries);
                        }
                        else
                        {
                            Logger.LogInfo($"Default branch for repository: {repo.GitHubRepoName} is ok.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to check or update default branch for repository: {repo.GitHubRepoName}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Migration failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Updates the migration status of a project based on the status of its repositories and teams.
    /// </summary>
    /// <param name="project">The migration project to update.</param>
    private void UpdateProjectStatus(MigrationProject project)
    {
        if (project.Repos.Count == 0 && project.Teams.Count == 0)
        {
            project.ProjectMigrationStatus = MigrationStatus.Skipped;
            return;
        }

        var allCompleted = true;
        var anyCompleted = false;
        var anyFailed = false;

        // Check repositories status
        foreach (var repo in project.Repos)
        {
            if (repo.GitHubRepoMigrationStatus == MigrationStatus.Completed)
                anyCompleted = true;
            else if (repo.GitHubRepoMigrationStatus == MigrationStatus.Failed)
                anyFailed = true;
            else if (repo.GitHubRepoMigrationStatus != MigrationStatus.Skipped)
                allCompleted = false;
        }

        // Check teams status
        foreach (var team in project.Teams)
        {
            if (team.GitHubTeamMigrationStatus == MigrationStatus.Completed)
                anyCompleted = true;
            else if (team.GitHubTeamMigrationStatus == MigrationStatus.Failed)
                anyFailed = true;
            else if (team.GitHubTeamMigrationStatus != MigrationStatus.Skipped)
                allCompleted = false;
        }

        if (anyFailed)
            project.ProjectMigrationStatus = allCompleted ? MigrationStatus.PartiallyCompleted : MigrationStatus.Failed;
        else if (allCompleted)
            project.ProjectMigrationStatus = anyCompleted ? MigrationStatus.Completed : MigrationStatus.Skipped;
        else if (anyCompleted)
            project.ProjectMigrationStatus = MigrationStatus.PartiallyCompleted;
    }
}