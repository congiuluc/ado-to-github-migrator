namespace AzureDevOps2GitHubMigrator.Models;

public class Repository
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsInMaintenance { get; set; }
    public long Size { get; set; }
    public string? DefaultBranch { get; set; }

    public string? Url { get; set; }
    public string? ProjectName { get; set; }
    public string RepositoryType { get; set; } = "git";

    public List<Branch> Branches { get; set; } = new();


}