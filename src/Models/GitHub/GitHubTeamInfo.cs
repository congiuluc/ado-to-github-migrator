namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubTeamInfo
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Privacy { get; set; }
    public string? Permission { get; set; }
    public string? Url { get; set; }
    public string? HtmlUrl { get; set; }
    public string? OrganizationName { get; set; }
}