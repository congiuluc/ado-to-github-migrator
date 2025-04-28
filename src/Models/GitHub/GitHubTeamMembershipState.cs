namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubTeamMembershipState
{
    public string? Username { get; set; }
    public string? State { get; set; }  // active, pending
    public string? Role { get; set; }   // member, maintainer
}