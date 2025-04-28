namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a migration project containing repositories and teams to be migrated.
/// </summary>
public class MigrationProject
{
    /// <summary>
    /// Gets or sets the unique identifier of the Azure DevOps project
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the Azure DevOps project
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the description of the Azure DevOps project
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the URL of the Azure DevOps project
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the current state of the Azure DevOps project
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the visibility setting of the Azure DevOps project
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    /// Gets or sets the name of the Azure DevOps organization containing this project
    /// </summary>
    public string? AdoOrganization { get; set; }

    /// <summary>
    /// Gets or sets the name of the target GitHub organization for migration
    /// </summary>
    public string? GitHubOrganization { get; set; }

    /// <summary>
    /// Gets or sets the list of repositories to be migrated
    /// </summary>
    public List<MigrationRepository> Repos { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of teams to be migrated
    /// </summary>
    public List<MigrationTeam> Teams { get; set; } = new();

    /// <summary>
    /// Gets or sets the overall migration status of the project
    /// </summary>
    public MigrationStatus ProjectMigrationStatus { get; set; } = MigrationStatus.Pending;
}