namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubMigrationResult
{
    public MigrationStatus? Status { get; set; }  // completed, failed
    public string? Error { get; set; }
    public DateTime? MigrationDate { get; set; }
    public List<GitHubMigratedMember> Members { get; set; } = new();
}