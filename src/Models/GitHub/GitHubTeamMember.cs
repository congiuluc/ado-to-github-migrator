namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubTeamMember
{
    public string? Id { get; set; }
    public string? Login { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public string? State { get; set; }
}