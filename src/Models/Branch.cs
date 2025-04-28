namespace AzureDevOps2GitHubMigrator.Models;

/// <summary>
/// Represents a branch in a version control repository
/// </summary>
public class Branch
{
    /// <summary>
    /// Gets or sets the name of the branch
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this branch is the default branch of the repository
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this branch is considered a base version
    /// </summary>
    public bool IsBaseVersion { get; set; }
}