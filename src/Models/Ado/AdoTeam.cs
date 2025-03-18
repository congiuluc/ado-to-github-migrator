using System.Text.Json.Serialization;

namespace AzureDevOps2GitHubMigrator.Models;

[JsonSerializable(typeof(AdoTeam))]
public class AdoTeam
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Descriptor { get; set; }

    public string? Url { get; set; }

    public string? ProjectName { get; set; }

    public List<AdoTeamMember> Members { get; set; } = new();




}