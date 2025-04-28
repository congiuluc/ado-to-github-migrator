namespace AzureDevOps2GitHubMigrator.Models;

public class ApiResponse<T>
{
    public int Count { get; set; }
    public List<T> Value { get; set; } = new();
}