namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubTeamRepositoryPermission
{
    public string? RepositoryName { get; set; }
    public string? Permission { get; set; }  // pull, push, admin, maintain, triage
    public bool Allowed { get; set; }
}