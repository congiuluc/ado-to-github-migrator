namespace AzureDevOps2GitHubMigrator.Models;

public enum MigrationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartiallyCompleted,
    Skipped
}