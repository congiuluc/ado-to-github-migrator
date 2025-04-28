using AzureDevOps2GitHubMigrator.Models.Ado;
using System.Text.Json.Serialization;

namespace AzureDevOps2GitHubMigrator.Models;

[JsonSerializable(typeof(AdoTeamMember))]
public class AdoTeamMember
{
    public bool IsTeamAdmin { get; set; }

    public AdoIdentity? Identity { get; set; }

}