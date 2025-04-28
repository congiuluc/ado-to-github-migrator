namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a version control repository in Azure DevOps that can be migrated to GitHub
/// </summary>
public class Repository
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
    /// Gets or sets a value indicating whether the repository is disabled
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repository is in maintenance mode
    /// </summary>
    public bool IsInMaintenance { get; set; }

    /// <summary>
    /// Gets or sets the size of the repository in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the default branch of the repository (e.g., 'main' or 'master')
    /// </summary>
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Gets or sets the URL of the repository in Azure DevOps
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the name of the Azure DevOps project containing this repository
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// Gets or sets the type of the repository. Defaults to "git"
    /// </summary>
    public string RepositoryType { get; set; } = "git";

    /// <summary>
    /// Gets or sets the list of branches in the repository
    /// </summary>
    public List<Branch> Branches { get; set; } = new();
}