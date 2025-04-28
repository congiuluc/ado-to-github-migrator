# Migrator Configuration File

This document provides an explanation of the `migrator_config.json` file, which is used to configure the Azure DevOps to GitHub migration process.

## Configuration Sections

### AzureDevOps
This section contains settings related to the Azure DevOps source environment.
- **Version**: Specifies the Azure DevOps version (e.g., `cloud` othe values can be `2022` `2020` `2019` `2017`).
- **BaseUrl**: The base URL for Azure DevOps (e.g., `https://dev.azure.com` or `https://mytfsserver:8080/tfs`).
- **Organization**: The name of the Azure DevOps organization/collection.
- **Pat**: Personal Access Token for authenticating with Azure DevOps.
- **Projects**: A comma-separated list of projects to migrate (e.g., `Project1,Project2,Project3`).

### GitHub
This section contains settings related to the GitHub target environment.
- **Organization**: The name of the GitHub organization.
- **Pat**: Personal Access Token for authenticating with GitHub.
- **DefaultTeamMemberRole**: Default role assigned to team members (e.g., `Admin` or `Member`).

### Migration
This section defines migration-specific settings.
- **MigrateTeams**: Boolean indicating whether to migrate teams.
- **RepoNamePattern**: Pattern for naming repositories during migration (e.g., `{projectName}-{repoName}`).
- **TeamNamePattern**: Pattern for naming teams during migration (e.g., `{projectName}-{teamName}`).
- **UsersMappingFile**: Path to the user mapping CSV file.

### Git
This section contains Git-specific settings.
- **DisableSSLVerify**: Boolean to disable SSL verification.
- **UsePatForClone**: Boolean indicating whether to use PAT for cloning repositories.

### WorkingDirectory
- **WorkingDirectory**: Path to the working directory for temporary files during migration.

## Notes
- Ensure that the Personal Access Tokens (PATs) are kept secure and not shared.
- Update the `UsersMappingFile` with the correct path to the user mapping CSV file.
- Modify the `Projects` field to include the projects you want to migrate.

## Example Usage
1. Update the `AzureDevOps` and `GitHub` sections with your organization details and PATs.
2. Specify the projects to migrate in the `Projects` field.
3. Run the migration tool using this configuration file.

For more details, refer to the tool's documentation.