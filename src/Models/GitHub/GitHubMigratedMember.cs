namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubMigratedMember
{
    public string? AdoEmail { get; set; }
    public string? GitHubUsername { get; set; }
    public MigrationStatus Status { get; set; }  
    public string? Error { get; set; }
}