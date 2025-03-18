namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubMigratedMember
{
    public string? AdoEmail { get; set; }
    public string? GitHubUsername { get; set; }
    public string? Status { get; set; }  // completed, failed
    public string? Error { get; set; }
}