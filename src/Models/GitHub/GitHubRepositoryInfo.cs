namespace AzureDevOps2GitHubMigrator.Models;

public class GitHubRepositoryInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Description { get; set; }
    public bool Private { get; set; }
    public string? DefaultBranch { get; set; }
    public string? Url { get; set; }
    public string? HtmlUrl { get; set; }
    public string? CloneUrl { get; set; }
    public string? Language { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PushedAt { get; set; }
    public bool Archived { get; set; }
    public bool Disabled { get; set; }
}
