namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a team member being migrated from Azure DevOps to GitHub
/// </summary>
public class MigrationTeamMember
{
    /// <summary>
    /// Gets or sets the unique identifier of the team member in Azure DevOps
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the team member
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the unique name (typically email) of the team member in Azure DevOps
    /// </summary>
    public string? UniqueName { get; set; }

    /// <summary>
    /// Gets or sets the email address of the team member
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member is a group rather than an individual user
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member has team administrator privileges
    /// </summary>
    public bool IsTeamAdmin { get; set; }

    /// <summary>
    /// Gets or sets the Azure DevOps URL for the team member
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the GitHub user ID after migration
    /// </summary>
    public string? GitHubUserId { get; set; }

    /// <summary>
    /// Gets or sets the GitHub username after migration
    /// </summary>
    public string? GitHubUserName { get; set; }

    /// <summary>
    /// Gets or sets the GitHub profile URL after migration
    /// </summary>
    public string? GitHubUserUrl { get; set; }

    /// <summary>
    /// Gets or sets the migration status of the GitHub user
    /// </summary>
    public MigrationStatus GitHubUserMigrationStatus { get; set; } = MigrationStatus.Pending;

    /// <summary>
    /// Gets or sets the error message if the GitHub user migration failed
    /// </summary>
    public string? GitHubUserMigrationError { get; set; }
}