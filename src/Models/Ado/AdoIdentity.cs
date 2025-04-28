using System.Text.Json.Serialization;

namespace AzureDevOps2GitHubMigrator.Models.Ado;

public class AdoIdentity
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? UniqueName { get; set; }
    public string? Url { get; set; }
    public bool IsContainer { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, string>? Properties { get; set; }
    public Dictionary<string, string>? ExternalIdentities { get; set; }
}