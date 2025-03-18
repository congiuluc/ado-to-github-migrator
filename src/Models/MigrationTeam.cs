namespace AzureDevOps2GitHubMigrator.Models;

public class MigrationTeam
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Description { get; set; }
    public List<MigrationTeamMember> Members { get; set; } = new();
    public string? GitHubTeamId { get; set; }
    public string? GitHubTeamName { get; set; }
    public string? GitHubTeamUrl { get; set; }
    public MigrationStatus GitHubTeamMigrationStatus { get; set; } = MigrationStatus.Pending;
    public string? GitHubTeamMigrationError { get; set; }
    public string? ProjectName { get; internal set; }
}