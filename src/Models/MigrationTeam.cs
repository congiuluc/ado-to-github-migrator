namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a team being migrated from Azure DevOps to GitHub
/// </summary>
public class MigrationTeam
{
    /// <summary>
    /// Gets or sets the unique identifier of the team
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the team
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the Azure DevOps URL for the team
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the description of the team
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of team members
    /// </summary>
    public List<MigrationTeamMember> Members { get; set; } = new();

    /// <summary>
    /// Gets or sets the GitHub team ID after migration
    /// </summary>
    public string? GitHubTeamId { get; set; }

    /// <summary>
    /// Gets or sets the GitHub team name after migration
    /// </summary>
    public string? GitHubTeamName { get; set; }

    /// <summary>
    /// Gets or sets the GitHub team role (defaults to "admin")
    /// </summary>
    public string? GitHubTeamRole { get; set; } = "admin";

    /// <summary>
    /// Gets or sets the GitHub team URL after migration
    /// </summary>
    public string? GitHubTeamUrl { get; set; }

    /// <summary>
    /// Gets or sets the migration status of the GitHub team
    /// </summary>
    public MigrationStatus GitHubTeamMigrationStatus { get; set; } = MigrationStatus.Pending;

    /// <summary>
    /// Gets or sets the error message if the GitHub team migration failed
    /// </summary>
    public string? GitHubTeamMigrationError { get; set; }

    /// <summary>
    /// Gets or sets the name of the project this team belongs to
    /// </summary>
    public string? ProjectName { get; internal set; }
}