namespace AzureDevOps2GitHubMigrator.Models;

public class Branch
{
    public string? Name { get; set; }
    public bool IsDefault { get; set; }

    public bool IsBaseVersion { get; set; }
}