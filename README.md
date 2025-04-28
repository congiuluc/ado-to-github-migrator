# Azure DevOps to GitHub Migrator

A comprehensive .NET tool for migrating repositories, teams, and projects from Azure DevOps to GitHub.

## Overview

This tool facilitates the migration of repositories, teams, and projects from Azure DevOps (both cloud and server versions) to GitHub. It handles Git repositories, team structures, and user mappings to ensure a smooth transition from Azure DevOps to GitHub.

## Features

- ✅ Migrate Git repositories from Azure DevOps to GitHub
- ✅ Support for Azure DevOps Cloud and Server versions (2017, 2019, 2020, 2022)
- ✅ Migrate team structures and permissions
- ✅ User mapping between Azure DevOps and GitHub
- ✅ Pre-migration assessment reports
- ✅ Detailed migration reporting
- ✅ Tool installation verification (Git, Git-TFS)
- ✅ Configurable naming patterns for repositories and teams
- ✅ Support for automated CI/CD migration planning
- ✅ Incremental migration capability
- ✅ Migration validation and verification reports

## Prerequisites

- .NET 9.0 SDK or runtime
- Git (v2.30.0 or later recommended)
- Git-TFS (for TFVC repositories)
- Personal Access Tokens with appropriate scopes:
  - **Azure DevOps PAT**: Requires Code (read), Project and team (read), User profile (read)
  - **GitHub PAT**: Requires repo, admin:org, admin:public_key scopes
- Windows, macOS, or Linux operating system
- Internet connectivity to both Azure DevOps and GitHub services

## Installation

### Option 1: Download the Release Package

1. Download the latest release from the [Releases page](URL-to-releases)
2. Extract the ZIP file to a directory of your choice
3. Run the tool from the command line

### Option 2: Build from Source

1. Clone the repository
   ```bash
   git clone https://github.com/[owner]/AzureDevOps2GitHubMigrator.git
   cd AzureDevOps2GitHubMigrator
   ```

2. Build the project
   ```bash
   dotnet build -c Release
   ```

3. Run the tool
   ```bash
   dotnet run --project src/AzureDevOps2GitHubMigrator.csproj
   ```

4. Create a deployable package (optional)
   ```bash
   dotnet publish -c Release -o ./publish
   ```

## Configuration

The migration process is controlled by a configuration file named `migrator_config.json`. You can use the template provided in the repository as a starting point.

### Configuration Sections

#### AzureDevOps
- **Version**: Specifies the Azure DevOps version (`cloud`, `2022`, `2020`, `2019`, `2017`)
- **BaseUrl**: The base URL for Azure DevOps (e.g., `https://dev.azure.com` or `https://mytfsserver:8080/tfs`)
- **Organization**: The name of the Azure DevOps organization/collection
- **Pat**: Personal Access Token for authenticating with Azure DevOps
- **Projects**: A comma-separated list of projects to migrate (e.g., `Project1,Project2,Project3`)

#### GitHub
- **Organization**: The name of the GitHub organization
- **Pat**: Personal Access Token for authenticating with GitHub
- **DefaultTeamMemberRole**: Default role assigned to team members (e.g., `admin` or `member`)

#### Migration
- **MigrateTeams**: Boolean indicating whether to migrate teams
- **RepoNamePattern**: Pattern for naming repositories (e.g., `{projectName}-{repoName}`)
- **TeamNamePattern**: Pattern for naming teams (e.g., `{projectName}-{teamName}`)
- **UsersMappingFile**: Path to the user mapping CSV file
- **UsePatForClone**: Boolean indicating whether to use PAT for cloning repositories
- **SkipConfirmation**: Boolean to skip interactive confirmation prompts

#### Git
- **DisableSSLVerify**: Boolean to disable SSL verification
- **UsePatForClone**: Boolean indicating whether to use PAT for Git clone operations

#### WorkingDirectory
- **WorkingDirectory**: Path to the working directory for temporary files during migration

### Sample Configuration

```json
{
  "AzureDevOps": {
    "Version": "cloud",
    "BaseUrl": "https://dev.azure.com",
    "Organization": "your-organization",
    "Pat": "your-ado-pat",
    "Projects": "Project1,Project2"
  },
  "GitHub": {
    "Organization": "your-github-org",
    "Pat": "your-github-pat",
    "DefaultTeamMemberRole": "member"
  },
  "Migration": {
    "MigrateTeams": true,
    "RepoNamePattern": "{projectName}-{repoName}",
    "TeamNamePattern": "{projectName}-{teamName}",
    "UsersMappingFile": "path/to/user-mapping.csv",
  },
  "Git": {
    "DisableSSLVerify": false,
    "UsePatForClone": false
  },
  "WorkingDirectory": "path/to/working/directory"
}
```

For a detailed explanation of all configuration options, refer to [ConfigurationGuide.md](ConfigurationGuide.md).

## Usage

The tool provides several commands for different stages of the migration process:

### Environment Validation

Check if the required tools are installed and properly configured:

```bash
AzureDevOps2GitHubMigrator check-git
AzureDevOps2GitHubMigrator check-git-tfs
```

### Tool Installation (Windows only)

Install required tools automatically on Windows systems:

```bash
AzureDevOps2GitHubMigrator install-git
AzureDevOps2GitHubMigrator install-git-tfs
```

### Pre-Migration Assessment

Assess Azure DevOps projects to identify repositories, teams, and potential migration issues:

```bash
AzureDevOps2GitHubMigrator ado-assessment --config-file path/to/migrator_config.json
```

Available options:
- `--ado-org`: Azure DevOps organization name
- `--ado-pat`: Azure DevOps Personal Access Token
- `--ado-version`: Azure DevOps version (e.g., cloud, 2022, 2020, 2019, 2017)
- `--ado-baseurl`: Azure DevOps Server base URL
- `--ado-projects`: Comma-separated list of project names
- `--output`: Output format (json or md)
- `--working-dir`: Working directory for output files
- `--config`: Path to the configuration file

### User Management

Export Azure DevOps users to create mapping file between Azure DevOps and GitHub users:

```bash
AzureDevOps2GitHubMigrator export-users --config-file path/to/migrator_config.json --output-file path/to/users.csv
```

Available options:
- `--ado-org`: Azure DevOps organization name
- `--ado-pat`: Azure DevOps Personal Access Token
- `--ado-version`: Azure DevOps version (e.g., cloud, 2022, 2020, 2019, 2017)
- `--ado-baseurl`: Azure DevOps Server base URL
- `--ado-projects`: Comma-separated list of project names
- `--output-file`: Output file path for the CSV
- `--config`: Path to the configuration file

### Migration Execution

Run the actual migration process to migrate repositories and teams from Azure DevOps to GitHub:

```bash
AzureDevOps2GitHubMigrator migrate --config-file path/to/migrator_config.json
```

Available options:
- `--ado-org`: Azure DevOps organization name
- `--ado-pat`: Azure DevOps Personal Access Token
- `--gh-org`: GitHub organization name
- `--gh-pat`: GitHub Personal Access Token
- `--ado-version`: Azure DevOps version (e.g., cloud, 2022, 2020, 2019, 2017)
- `--ado-baseurl`: Azure DevOps Server base URL
- `--ado-projects`: Comma-separated list of project names
- `--repo-name-pattern`: Pattern for GitHub repository names
- `--team-name-pattern`: Pattern for GitHub team names
- `--migrate-teams`: Whether to migrate Azure DevOps teams to GitHub teams
- `--skip-confirmation`: Skip confirmation prompt before migration
- `--users-mapping-file`: Path to the users mapping file
- `--git-disable-ssl-verify`: Disable SSL verification for Git operations
- `--use-pat-for-clone`: Use PAT for Git clone operations
- `--working-dir`: Working directory for temporary files
- `--config`: Path to the configuration file

### Post-Migration Reporting

Generate migration reports to verify migration success and identify any issues:

```bash
AzureDevOps2GitHubMigrator migration-report --config-file path/to/migrator_config.json --output-file path/to/report.md
```

## User Mapping

User mapping is essential for preserving team memberships and repository permissions during migration. Create a CSV file with the following format:

```csv
AzureDevOpsEmail,GitHubUsername
user1@example.com,github-user1
user2@example.com,github-user2
```

The `export-users` command helps generate the initial template with Azure DevOps users that you can then map to their GitHub usernames.

## Step-by-Step Migration Process

1. **Environment Setup**:
   - Install prerequisites (.NET 9.0, Git, Git-TFS)
   - Verify installation using `check-git` and `check-git-tfs` commands
   - Prepare Azure DevOps PAT with necessary scopes
   - Prepare GitHub PAT with necessary scopes

2. **Pre-Migration Assessment**:
   - Run the assessment command to analyze your Azure DevOps projects
   - Review the assessment report to understand the scope of the migration
   - Identify any potential issues with repositories or team structures

3. **User Mapping**:
   - Export Azure DevOps users using the `export-users` command
   - Map Azure DevOps users to their corresponding GitHub usernames
   - Validate the mapping file format and completeness

4. **Configuration Setup**:
   - Create or update the `migrator_config.json` file with your settings
   - Configure repository and team naming patterns
   - Set migration options (team migration, SSL verification, etc.)

5. **Migration Execution**:
   - Run the migration command using your configuration file
   - Review the migration summary before confirming (unless `skip-confirmation` is enabled)
   - Monitor the migration progress

6. **Post-Migration Verification**:
   - Generate a migration report to verify all repositories and teams were migrated correctly
   - Check GitHub organization for the migrated repositories and teams
   - Verify repository content and team memberships

7. **Cleanup and Final Steps**:
   - Review any warnings or errors in the migration report
   - Perform any necessary manual adjustments in GitHub
   - Update documentation and inform team members about the new GitHub URLs

## Migration Performance Considerations

- **Repository Size**: Large repositories require more time and disk space for migration
- **Network Bandwidth**: Migration speed depends on network bandwidth between your machine, Azure DevOps, and GitHub
- **API Rate Limits**: Both Azure DevOps and GitHub have API rate limits that may affect migration speed
- **Memory Requirements**: Large migrations may require significant memory, especially for team migrations with many members
- **Working Directory**: Ensure sufficient disk space in the working directory for repository clones

## Parallel Migration

For large organizations, you can run multiple instances of the migrator on different machines:

1. Split projects across multiple configuration files
2. Run the migrator on different machines using different configuration files
3. Use different working directories to avoid conflicts

## Common Issues and Solutions

### Authentication Issues
- **Issue**: `Authentication failed` errors
- **Solution**: Verify PAT scopes and expiration dates

### Repository Migration Failures
- **Issue**: `Unable to push to GitHub repository`
- **Solution**: Check GitHub organization permissions and repository existence

### Team Migration Issues
- **Issue**: `Team members not migrated`
- **Solution**: Verify user mapping file and GitHub user existence

### Rate Limiting
- **Issue**: `API rate limit exceeded` errors
- **Solution**: Increase intervals between requests or split migration into smaller batches

### Large Repository Issues
- **Issue**: Migration times out for very large repositories
- **Solution**: Increase timeout settings and ensure sufficient disk space

### SSL Certificate Issues
- **Issue**: SSL certificate validation errors
- **Solution**: Use the `--git-disable-ssl-verify` option if working in environments with self-signed certificates

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute to this project.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

If you encounter any issues or have questions about using the Azure DevOps to GitHub Migrator, please file an issue in the GitHub repository.