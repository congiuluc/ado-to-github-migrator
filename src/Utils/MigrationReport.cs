using System.Text;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;
namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Handles the generation of migration assessment and status reports.
/// This class provides detailed reports about repository and team migration status.
/// </summary>
/// <remarks>
/// Report types include:
/// - Pre-migration assessment reports
/// - Migration progress reports
/// - Post-migration validation reports
/// - Error and warning summaries
/// </remarks>
public class MigrationReport
{
    private readonly StringBuilder _content;
    private readonly string _timestamp;
    private int _indentLevel;
    private readonly bool _isMarkdown;
    private readonly Dictionary<string, int> _stats;

    /// <summary>
    /// Initializes a new migration report
    /// </summary>
    /// <param name="isMarkdown">Whether to format output as Markdown</param>
    public MigrationReport(bool isMarkdown = true)
    {
        _content = new StringBuilder();
        _timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
        _indentLevel = 0;
        _isMarkdown = isMarkdown;
        _stats = new Dictionary<string, int>();
    }

    /// <summary>
    /// Adds a header section to the report
    /// </summary>
    /// <param name="text">Header text</param>
    /// <param name="level">Header level (1-6)</param>
    public void AddHeader(string text, int level = 1)
    {
        if (_isMarkdown)
        {
            _content.AppendLine($"{new string('#', level)} {text}\n");
        }
        else
        {
            _content.AppendLine($"{text}\n{new string('=', text.Length)}\n");
        }
    }

    /// <summary>
    /// Adds project migration information to the report
    /// </summary>
    /// <param name="project">Migration project details</param>
    public void AddProject(MigrationProject project)
    {
        AddHeader($"Project: {project.Name}", 2);
        
        IncrementStat("Total Projects");
        AddDetail("Name", project.Name ?? "Unnamed Project");
        AddDetail("Repository Count", project.Repos?.Count.ToString() ?? "0");
        AddDetail("Team Count", project.Teams?.Count.ToString() ?? "0");
        
        if (project.Repos != null)
        {
            AddRepositories(project.Repos);
        }
        
        if (project.Teams != null)
        {
            AddTeams(project.Teams);
        }
        
        _content.AppendLine();
    }

    /// <summary>
    /// Adds repository migration details to the report
    /// </summary>
    /// <param name="repositories">List of repositories being migrated</param>
    private void AddRepositories(ICollection<MigrationRepository> repositories)
    {
        if (repositories?.Any() != true) return;

        AddHeader("Repositories", 3);
        IncrementStat("Total Repositories", repositories.Count);

        foreach (var repo in repositories)
        {
            _indentLevel++;
            AddDetail("Name", repo.Name ?? "Unnamed Repository");
            AddDetail("Type", repo.RepositoryType?.ToUpper() ?? "GIT");
            AddDetail("Size", Common.FormatSize(repo.Size));
            AddDetail("Default Branch", repo.DefaultBranch ?? "N/A");
            AddDetail("Migration Status", GetStatusEmoji(repo.GitHubRepoMigrationStatus));
            
            if (repo.GitHubRepoMigrationStatus == MigrationStatus.Failed)
            {
                AddDetail("Error", repo.GitHubRepoMigrationError ?? "Unknown error");
                IncrementStat("Failed Repositories");
            }
            else if (repo.GitHubRepoMigrationStatus == MigrationStatus.Completed)
            {
                IncrementStat("Migrated Repositories");
            }
            
            _content.AppendLine();
            _indentLevel--;
        }
    }

    /// <summary>
    /// Adds team migration details to the report
    /// </summary>
    /// <param name="teams">List of teams being migrated</param>
    private void AddTeams(ICollection<MigrationTeam> teams)
    {
        if (teams?.Any() != true) return;

        AddHeader("Teams", 3);
        IncrementStat("Total Teams", teams.Count);

        foreach (var team in teams)
        {
            _indentLevel++;
            AddDetail("Name", team.Name ?? "Unnamed Team");
            AddDetail("Member Count", team.Members?.Count.ToString() ?? "0");
            AddDetail("Migration Status", GetStatusEmoji(team.GitHubTeamMigrationStatus));
            
            if (team.GitHubTeamMigrationStatus == MigrationStatus.Failed)
            {
                AddDetail("Error", team.GitHubTeamMigrationError ?? "Unknown error");
                IncrementStat("Failed Teams");
            }
            else if (team.GitHubTeamMigrationStatus == MigrationStatus.Completed)
            {
                IncrementStat("Migrated Teams");
            }
            
            _content.AppendLine();
            _indentLevel--;
        }
    }

    /// <summary>
    /// Adds a detail line to the report with proper indentation
    /// </summary>
    /// <param name="label">Detail label</param>
    /// <param name="value">Detail value</param>
    private void AddDetail(string label, string value)
    {
        var indent = new string(' ', _indentLevel * 2);
        if (_isMarkdown)
        {
            _content.AppendLine($"{indent}- **{label}**: {value}");
        }
        else
        {
            _content.AppendLine($"{indent}{label}: {value}");
        }
    }

    /// <summary>
    /// Gets an emoji representation of migration status
    /// </summary>
    /// <param name="status">Migration status</param>
    /// <returns>Status with emoji indicator</returns>
    private string GetStatusEmoji(MigrationStatus status) => status switch
    {
        MigrationStatus.Pending => "⏳ Pending",
        MigrationStatus.InProgress => "🔄 In Progress",
        MigrationStatus.Completed => "✅ Completed",
        MigrationStatus.Failed => "❌ Failed",
        _ => "❓ Unknown"
    };

    /// <summary>
    /// Increments a statistics counter
    /// </summary>
    /// <param name="key">Statistic name</param>
    /// <param name="amount">Amount to increment by</param>
    private void IncrementStat(string key, int amount = 1)
    {
        if (!_stats.ContainsKey(key))
            _stats[key] = 0;
        _stats[key] += amount;
    }

    /// <summary>
    /// Adds migration statistics summary to the report
    /// </summary>
    public void AddStatistics()
    {
        AddHeader("Migration Statistics", 2);
        foreach (var stat in _stats.OrderBy(s => s.Key))
        {
            AddDetail(stat.Key, stat.Value.ToString());
        }
        _content.AppendLine();
    }

    /// <summary>
    /// Saves the report to a file
    /// </summary>
    /// <param name="workerCount">Number of parallel workers used</param>
    /// <returns>Path to the saved report file</returns>
    public async Task<string> SaveAsync(int workerCount)
    {
        var extension = _isMarkdown ? "md" : "txt";
        var filename = $"{_timestamp}_{workerCount}_assessment.{extension}";
        await File.WriteAllTextAsync(filename, _content.ToString());
        return filename;
    }

    // Dictionary mapping migration statuses to their respective emoji icons for visual representation
    private static readonly Dictionary<MigrationStatus, string> StatusIcons = new()
    {
        { MigrationStatus.Completed, "✅" },
        { MigrationStatus.Pending, "⏳" },
        { MigrationStatus.Skipped, "⏭️" },
        { MigrationStatus.Failed, "❌" },
        { MigrationStatus.PartiallyCompleted, "⚠️" },
        { MigrationStatus.InProgress, "🔄" }
    };

    /// <summary>
    /// Generates a detailed markdown report of the migration process, including statistics and status for all components.
    /// </summary>
    /// <param name="projects">List of projects being migrated</param>
    /// <param name="adoOrg">Azure DevOps organization name</param>
    /// <param name="adoUrl">Azure DevOps base URL</param>
    /// <param name="githubOrg">GitHub organization name</param>
    /// <param name="outputFile">Output file path for the report</param>
    public static void GenerateMarkdownReport(
        List<MigrationProject> projects,
        string adoOrg,
        string adoUrl,
        string githubOrg,
        string outputFile)
    {
        try
        {
            // Initialize report generation with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var report = new StringBuilder();

            // Header
            report.AppendLine("# Migration Execution Report");
            report.AppendLine($"Generated on: {timestamp}");
            report.AppendLine();

            // Migration Environment
            report.AppendLine("## Migration Environment");
            report.AppendLine($"- Azure DevOps Organization: {adoOrg}");
            report.AppendLine($"- Azure DevOps URL: {adoUrl}");
            report.AppendLine($"- GitHub Organization: {githubOrg}");
            report.AppendLine($"- GitHub URL: https://github.com/orgs/{githubOrg}");
            report.AppendLine();

            // Status Legend
            report.AppendLine("## Status Legend");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Completed]} Migrated");
            report.AppendLine($"- {StatusIcons[MigrationStatus.PartiallyCompleted]} Partially Migrated");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Pending]} Pending");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Skipped]} Skipped");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Failed]} Failed");
            report.AppendLine($"- {StatusIcons[MigrationStatus.InProgress]} In Progress");
            report.AppendLine();

            // Overall Statistics - Track progress across all projects
            report.AppendLine("## Migration Statistics");
            report.AppendLine();
            report.AppendLine("### Overall Progress");
            report.AppendLine($"Total Projects: {projects.Count}");
            
            // Count and display projects by their migration status
            report.AppendLine($"- {StatusIcons[MigrationStatus.Completed]} Successfully Migrated: {projects.Count(p => p.ProjectMigrationStatus == MigrationStatus.Completed)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.PartiallyCompleted]} Partially Migrated: {projects.Count(p => p.ProjectMigrationStatus == MigrationStatus.PartiallyCompleted)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Pending]} Pending Migration: {projects.Count(p => p.ProjectMigrationStatus == MigrationStatus.Pending)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Failed]} Failed: {projects.Count(p => p.ProjectMigrationStatus == MigrationStatus.Failed)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Skipped]} Skipped: {projects.Count(p => p.ProjectMigrationStatus == MigrationStatus.Skipped)}");
            report.AppendLine();

            // Repository Statistics - Aggregate all repositories across projects
            var allRepos = projects.SelectMany(p => p.Repos).ToList();
            report.AppendLine("### Repository Statistics");
            report.AppendLine($"Total Repositories: {allRepos.Count}");
            report.AppendLine();
            
            // Display repositories migration status breakdown
            report.AppendLine("Migration Status:");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Completed]} Successfully Migrated: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Completed)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Pending]} Pending Migration: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Pending)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Failed]} Failed Migration: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Failed)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.Skipped]} Skipped: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Skipped)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.PartiallyCompleted]} Partially Migrated: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.PartiallyCompleted)}");
            report.AppendLine($"- {StatusIcons[MigrationStatus.InProgress]} In Progress: {allRepos.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.InProgress)}");
            report.AppendLine();

            // Display repository type distribution
            report.AppendLine("Repository Types:");
            report.AppendLine($"- Git Repositories: {allRepos.Count(r => r.RepositoryType.Equals("git", StringComparison.OrdinalIgnoreCase))}");
            report.AppendLine($"- TFVC Repositories: {allRepos.Count(r => r.RepositoryType.Equals("tfvc", StringComparison.OrdinalIgnoreCase))}");
            report.AppendLine();

            // Team Statistics - Only included if teams exist
            var allTeams = projects.SelectMany(p => p.Teams).ToList();
            if (allTeams.Any())
            {
                report.AppendLine("### Team Statistics");
                report.AppendLine($"Total Teams: {allTeams.Count}");
                
                // Display team migration status breakdown
                report.AppendLine($"- {StatusIcons[MigrationStatus.Completed]} Successfully Migrated: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.Completed)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Pending]} Pending Migration: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.Pending)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Failed]} Failed Migration: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.Failed)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Skipped]} Skipped: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.Skipped)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.PartiallyCompleted]} Partially Migrated: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.PartiallyCompleted)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.InProgress]} In Progress: {allTeams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.InProgress)}");
                report.AppendLine();

                var allMembers = allTeams.SelectMany(t => t.Members).ToList();
                report.AppendLine("Team Members:");
                report.AppendLine($"Total Members: {allMembers.Count}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Completed]} Successfully Migrated: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Completed)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Pending]} Pending Migration: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Pending)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Failed]} Failed Migration: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Failed)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.Skipped]} Skipped: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Skipped)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.PartiallyCompleted]} Partially Migrated: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.PartiallyCompleted)}");
                report.AppendLine($"- {StatusIcons[MigrationStatus.InProgress]} In Progress: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.InProgress)}");
                report.AppendLine();
            }

            // Project Details
            report.AppendLine("## Project Details");
            foreach (var project in projects)
            {
                var projectStatus = StatusIcons[project.ProjectMigrationStatus];
                report.AppendLine($"### {projectStatus} {project.Name}");
                report.AppendLine($"- Status: {projectStatus} {project.ProjectMigrationStatus}");
                report.AppendLine();

                // Repository Details
                report.AppendLine("#### Repository Details");
                report.AppendLine();
                report.AppendLine("| Status | Azure DevOps Repository | Type | Default Branch | GitHub Repository | GitHub URL | Error |");
                report.AppendLine("|--------|----------------------|------|----------------|------------------|------------|--------|");

                foreach (var repo in project.Repos)
                {
                    var status = StatusIcons[repo.GitHubRepoMigrationStatus];
                    var githubUrl = !string.IsNullOrEmpty(repo.GitHubRepoUrl) ? Common.CreateClickableMarkdownLink(repo.GitHubRepoName ?? "", repo.GitHubRepoUrl) : "N/A";
                    var errorMessage = !string.IsNullOrEmpty(repo.GitHubRepoMigrationError) ? repo.GitHubRepoMigrationError.Replace("|", "/") : "-";
                    report.AppendLine($"| {status} | {repo.Name} | {repo.RepositoryType.ToUpper()} | {repo.DefaultBranch ?? "N/A"} | {repo.GitHubRepoName ?? "-"} | {githubUrl} | {errorMessage} |");
                }
                report.AppendLine();

                // Team Details
                if (project.Teams.Any())
                {
                    report.AppendLine("#### Team Migration Details");
                    report.AppendLine();
                    report.AppendLine("| Status | Team Name | GitHub Team | Members | GitHub Team URL | Error |");
                    report.AppendLine("|--------|-----------|-------------|---------|----------------|--------|");

                    foreach (var team in project.Teams)
                    {
                        var status = StatusIcons[team.GitHubTeamMigrationStatus];
                        var teamUrl = !string.IsNullOrEmpty(team.GitHubTeamUrl) ? Common.CreateClickableMarkdownLink(team.GitHubTeamName ?? "", team.GitHubTeamUrl) : "N/A";
                        var memberCount = $"{team.Members.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Completed)}/{team.Members.Count}";
                        var errorMessage = !string.IsNullOrEmpty(team.GitHubTeamMigrationError) ? team.GitHubTeamMigrationError.Replace("|", "/") : "-";
                        report.AppendLine($"| {status} | {team.Name} | {team.GitHubTeamName ?? "-"} | {memberCount} | {teamUrl} | {errorMessage} |");
                    }
                    report.AppendLine();

                    // Team Member Details
                    foreach (var team in project.Teams.Where(t => t.Members.Any()))
                    {
                        report.AppendLine($"#### Team: {team.Name} Member Details");
                        report.AppendLine();
                        report.AppendLine("| Status | Member Email | GitHub Username | Admin | Error |");
                        report.AppendLine("|--------|--------------|----------------|-------|--------|");

                        foreach (var member in team.Members)
                        {
                            var status = StatusIcons[member.GitHubUserMigrationStatus];
                            var errorMessage = !string.IsNullOrEmpty(member.GitHubUserMigrationError) ? member.GitHubUserMigrationError.Replace("|", "/") : "-";
                            var isAdmin = member.IsTeamAdmin ? "Yes" : "No"; // Using the correct property
                            report.AppendLine($"| {status} | {member.UniqueName} | {member.GitHubUserName ?? "-"} | {isAdmin} | {errorMessage} |");
                        }
                        report.AppendLine();
                    }
                }
            }

            // Save report to file
            File.WriteAllText(outputFile, report.ToString(), Encoding.UTF8);
            Logger.LogSuccess($"Report saved to: {outputFile}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error generating migration report: {ex.Message}", ex);
            if (ex.InnerException != null)
                Logger.LogError($"Details: {ex.InnerException.Message}", ex.InnerException);
            throw;
        }
    }

    /// <summary>
    /// Writes a summary completion report of the migration process, including statistics and status for all components.
    /// </summary>
    /// <param name="projects">List of projects being migrated</param>
    /// <param name="outputFile">Output file path for the report</param>
    public static void WriteCompletionReport(
        List<Models.MigrationProject> projects,
        string outputFile = "migration_report.md")
    {
        var repositories = projects.SelectMany(p => p.Repos).ToList();
        var teams = projects.SelectMany(p => p.Teams).ToList();
        
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var report = new StringBuilder();
        
        report.AppendLine("# Azure DevOps to GitHub Migration Report");
        report.AppendLine($"Generated: {timestamp}");
        report.AppendLine();
        
        // Print report file path with clickable link in console
        var fullPath = Path.GetFullPath(outputFile);

        
        // Repositories section
        report.AppendLine("## Migration Summary");
        report.AppendLine();
        report.AppendLine("### Repositories");
        report.AppendLine();
        report.AppendLine("| Project | Repository | Type | Status | Error |");
        report.AppendLine("|---------|------------|------|--------|-------|");

        foreach (var repo in repositories)
        {
            var status = repo.GitHubRepoMigrationStatus == MigrationStatus.Completed ? "✅ Migrated" : "❌ Failed";
            var errorMsg = !string.IsNullOrEmpty(repo.GitHubRepoMigrationError) ? repo.GitHubRepoMigrationError.Replace("|", "\\|") : "-";
            var type = repo.RepositoryType?.ToUpper() ?? "GIT";
            report.AppendLine($"| {repo.ProjectName} | {repo.Name} | {type} | {status} | {errorMsg} |");
        }

        // Teams section
        report.AppendLine();
        report.AppendLine("### Teams and Members");
        report.AppendLine();

        if (teams != null && teams.Any())
        {
            report.AppendLine("| Project | Team | Status | Members Migrated | Error |");
            report.AppendLine("|---------|------|--------|-----------------|--------|");

            foreach (var team in teams)
            {
                var status = team.GitHubTeamMigrationStatus == MigrationStatus.Completed ? "✅ Migrated" : "❌ Failed";
                var membersCount = team.Members != null 
                    ? $"{team.Members.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Completed)}/{team.Members.Count()}"
                    : "N/A";
                var errorMsg = !string.IsNullOrEmpty(team.GitHubTeamMigrationError) ? team.GitHubTeamMigrationError.Replace("|", "\\|") : "-";
                report.AppendLine($"| {team.ProjectName} | {team.Name} | {status} | {membersCount} | {errorMsg} |");

                if (team.Members?.Any() == true)
                {
                    report.AppendLine();
                    report.AppendLine($"#### Team: {team.Name} Members");
                    report.AppendLine();
                    report.AppendLine("| Email | GitHub Username | Status | Error |");
                    report.AppendLine("|-------|----------------|--------|--------|");

                    foreach (var member in team.Members)
                    {
                        var memberStatus = member.GitHubUserMigrationStatus == MigrationStatus.Completed ? "✅ Migrated" : "❌ Failed";
                        var memberError = !string.IsNullOrEmpty(member.GitHubUserMigrationError) ? member.GitHubUserMigrationError.Replace("|", "\\|") : "-";
                        report.AppendLine($"| {member.UniqueName} | {member.GitHubUserName ?? "-"} | {memberStatus} | {memberError} |");
                    }
                    report.AppendLine();
                }
            }
        }
        else
        {
            report.AppendLine("Team migration was not enabled.");
        }

        // Statistics section
        report.AppendLine();
        report.AppendLine("## Statistics");
        report.AppendLine($"- Total Repositories: {repositories.Count()}");
        report.AppendLine($"- Successfully Migrated Repositories: {repositories.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Completed)}");
        report.AppendLine($"- Failed Repositories: {repositories.Count(r => r.GitHubRepoMigrationStatus == MigrationStatus.Failed)}");

        if (teams != null)
        {
            report.AppendLine($"- Total Teams: {teams.Count()}");
            report.AppendLine($"- Successfully Migrated Teams: {teams.Count(t => t.GitHubTeamMigrationStatus == MigrationStatus.Completed)}");
            report.AppendLine($"- Failed Teams: {teams.Count(t => t.GitHubTeamMigrationStatus == Models.MigrationStatus.Failed)}");

            var allMembers = teams.SelectMany(t => t.Members ?? Enumerable.Empty<Models.MigrationTeamMember>());
            if (allMembers.Any())
            {
                report.AppendLine($"- Total Team Members: {allMembers.Count()}");
                report.AppendLine($"- Successfully Migrated Members: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Completed)}");
                report.AppendLine($"- Failed Members: {allMembers.Count(m => m.GitHubUserMigrationStatus == MigrationStatus.Failed)}");
            }
        }

        // Save report to file
        File.WriteAllText(outputFile, report.ToString(), Encoding.UTF8);
        
        Logger.LogSuccess($"\nReport saved to: {outputFile}");
    }
}