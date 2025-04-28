namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a repository being migrated from Azure DevOps to GitHub
/// </summary>
public class MigrationRepository
{
    /// <summary>
    /// Gets or sets the unique identifier of the repository
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the repository
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the name of the project this repository belongs to
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// Gets or sets the Azure DevOps URL for the repository
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repository is disabled
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repository is in maintenance mode
    /// </summary>
    public bool IsInMaintenance { get; set; }

    /// <summary>
    /// Gets or sets the visibility setting of the repository
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    /// Gets or sets the total number of branches in the repository
    /// </summary>
    public int BranchCount { get; set; }

    /// <summary>
    /// Gets or sets the name of the default branch
    /// </summary>
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Gets or sets the size of the repository in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the type of the repository (defaults to "git")
    /// </summary>
    public string RepositoryType { get; set; } = "git";

    /// <summary>
    /// Gets or sets the GitHub repository ID after migration
    /// </summary>
    public string? GitHubRepoId { get; set; }

    /// <summary>
    /// Gets or sets the GitHub repository name after migration
    /// </summary>
    public string? GitHubRepoName { get; set; }

    /// <summary>
    /// Gets or sets the GitHub repository URL after migration
    /// </summary>
    public string? GitHubRepoUrl { get; set; }

    /// <summary>
    /// Gets or sets the migration status of the GitHub repository
    /// </summary>
    public MigrationStatus GitHubRepoMigrationStatus { get; set; } = MigrationStatus.Pending;

    /// <summary>
    /// Gets or sets the error message if the GitHub repository migration failed
    /// </summary>
    public string? GitHubRepoMigrationError { get; set; }
}