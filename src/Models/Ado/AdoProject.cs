using System.Text.Json.Serialization;

namespace AzureDevOps2GitHubMigrator.Models;

[JsonSerializable(typeof(AdoProject))]
public class AdoProject
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? State { get; set; }
    public string? Visibility { get; set; }

    public string? Url { get; set; }

    public string? AdoOrganization { get; set; }

    public List<Repository> Repos { get; set; } = new();
    public List<AdoTeam> Teams { get; set; } = new();

}