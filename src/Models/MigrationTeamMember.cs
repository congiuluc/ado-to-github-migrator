namespace AzureDevOps2GitHubMigrator.Models;

public class MigrationTeamMember
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? UniqueName { get; set; }
    public string? Email { get; set; }
    public bool IsGroup { get; set; }
    public bool IsTeamAdmin { get; set; }
    public string? Url { get; set; }
    public string? GitHubUserId { get; set; }
    public string? GitHubUserName { get; set; }
    public string? GitHubUserUrl { get; set; }
    public MigrationStatus GitHubUserMigrationStatus { get; set; } = MigrationStatus.Pending;
    public string? GitHubUserMigrationError { get; set; }
}