namespace AzureDevOps2GitHubMigrator.Models;

public class MigrationProject
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? State { get; set; }
    public string? Visibility { get; set; }
    public string? AdoOrganization { get; set; }
    public string? GitHubOrganization { get; set; }
    public List<MigrationRepository> Repos { get; set; } = new();
    public List<MigrationTeam> Teams { get; set; } = new();
    public MigrationStatus ProjectMigrationStatus { get; set; } = MigrationStatus.Pending;
}