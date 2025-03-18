namespace AzureDevOps2GitHubMigrator.Models;

public class MigrationRepository
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ProjectName { get; set; }
    public string? Url { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsInMaintenance { get; set; }
    public string? Visibility { get; set; }
    public int BranchCount { get; set; }
    public string? DefaultBranch { get; set; }
    public long Size { get; set; }
    public string RepositoryType { get; set; } = "git";
    public string? GitHubRepoId { get; set; }
    public string? GitHubRepoName { get; set; }
    public string? GitHubRepoUrl { get; set; }
    public MigrationStatus GitHubRepoMigrationStatus { get; set; } = MigrationStatus.Pending;
    public string? GitHubRepoMigrationError { get; set; }
}