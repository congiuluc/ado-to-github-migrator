using System.Text.Json.Serialization;

namespace AzureDevOps2GitHubMigrator.Models;

[JsonSerializable(typeof(AdoIdentity))]
public class AdoIdentity
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? UniqueName { get; set; }
    public string? Url { get; set; }
    public bool IsContainer { get; set; }
}