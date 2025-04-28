# Azure DevOps to GitHub Migrator 🚀

A comprehensive tool for migrating repositories, teams, and users from Azure DevOps to GitHub. This tool helps organizations smoothly transition their development workflow while preserving their project structure and team organization.

## Features

### Pre-migration Assessment ✨
- `ado-assessment`: Analyzes Azure DevOps projects and generates a detailed assessment report
- Evaluates repository types (Git/TFVC)
- Identifies potential migration challenges
- Generates detailed reports in JSON or Markdown format

### Repository Migration 📦
- Supports both Git and TFVC repositories
- Preserves commit history and branch structure
- Configurable repository naming patterns with placeholders:
  - {projectName}: Azure DevOps project name
  - {repoName}: Original repository name
- Branch protection rules migration
- Handles large repositories efficiently with parallel processing

### Team Migration 🤝
- Migrates team structures from Azure DevOps to GitHub
- Configurable team naming patterns with placeholders:
  - {projectName}: Azure DevOps project name
  - {teamName}: Original team name
- Preserves team memberships
- Support for custom team member roles
- Automated permission mapping

### System Requirements Verification 🔍
- `check-git`: Verifies Git installation
- `check-tfvc`: Verifies Git-TFS installation
- `install-git`: Automated Git installation
- `install-gittfs`: Automated Git-TFS installation

### Reporting and Monitoring 📊
- `migration-report`: Generates detailed migration status reports
- Real-time progress tracking
- Detailed success/failure reporting for each component
- Migration statistics and metrics
- Support for both JSON and Markdown report formats

## Installation 💻

1. Ensure you have .NET 9.0 or later installed
2. Clone this repository
3. Build the solution:
   ```bash
   dotnet build src/AzureDevOps2GitHubMigrator.sln
   ```

## Configuration ⚙️

Create a `migrator_config.json` file with the following structure:

```json
{
  "AzureDevOps": {
    "Version": "cloud",  // or "2019", "2020", "2022"
    "BaseUrl": "https://dev.azure.com",
    "Organization": "your-org",
    "Pat": "your-ado-pat",
    "DefaultTeamMemberRole": "admin",  // or "member"
    "Projects": "Project1,Project2,Project3"  // Comma-separated list of projects
  },
  "GitHub": {
    "Organization": "your-github-org",
    "Pat": "your-github-pat"
  },
  "Migration": {
    "IncludeTeams": true,
    "RepoNamePattern": "{projectName}-{repoName}",
    "TeamNamePattern": "{projectName}-{teamName}",
    "DryRun": false,
    "ParallelOperations": 4,
    "UsersMappingFile": "path/to/users_mapping.csv"  // Optional: Path to user mapping file
  },
  "WorkingDirectory": "path/to/working/dir"
}
```

### User Mapping File Format 👥

When migrating teams, you need to provide a CSV file that maps Azure DevOps users to their corresponding GitHub accounts. The file should follow this format:

```csv
AdoUser,Name,Email,GitHubUser
user@company.com,John Doe,johndoe@github.com,johndoe
jane.smith@company.com,Jane Smith,jane.smith@github.com,jsmith
```

For organizations with SAML SSO enabled, the format includes an additional SAML identity column:

```csv
AdoUser,Name,Email,GitHubUser,SAML_Identity
user@company.com,John Doe,johndoe@github.com,johndoe,user@company.com
jane.smith@company.com,Jane Smith,jane.smith@github.com,jsmith,jane.smith@company.com
```

#### Column Descriptions:
- `AdoUser`: User's Azure DevOps User Principal Name (usually email)
- `Name`: Display name in Azure DevOps
- `Email`: User's GitHub email address
- `GitHubUser`: GitHub login/username
- `SAML_Identity`: (Optional) SAML identity for SSO-enabled organizations

You can generate this mapping file automatically using the following command:
```bash
dotnet run -- user-mapping --ado-org your-org --ado-pat your-pat --gh-org your-github-org --gh-pat your-github-pat
```

### Migration Process with User Mapping 🔄

1. Generate the user mapping file using the command above
2. Review and update the mappings as needed
3. Specify the mapping file path in the configuration:
   - Either in `migrator_config.json` under `Migration.UsersMappingFile`
   - Or using the `--users-mapping-file` parameter in the migration command

> **Note**: The user mapping file is required when migrating teams (`--migrate-teams true`). Without it, team migrations will be skipped.

## Usage 🔨

### Interactive Mode 💬
Run the tool without arguments to enter interactive mode:
```bash
dotnet run --project src/AzureDevOps2GitHubMigrator.csproj
```

### Command Line Mode ⌨️

#### Run Assessment
```bash
dotnet run -- ado-assessment --ado-org your-org --ado-pat your-pat --ado-projects project1,project2
```

#### Export Users
```bash
dotnet run -- ado-export-users --ado-org your-org --ado-pat your-pat
```

#### Export SAML Users
```bash
dotnet run -- gh-export-saml-users --gh-org your-org --gh-pat your-pat
```

#### Generate Migration Report
```bash
dotnet run -- migration-report --ado-org your-org --ado-pat your-pat --gh-org your-github-org --gh-pat your-github-pat
```

### Command Parameters 🎯

#### Assessment Command
- Required:
  - `--ado-org`: Azure DevOps organization name
  - `--ado-pat`: Azure DevOps Personal Access Token
- Optional:
  - `--ado-projects`: Comma-separated list of project names
  - `--ado-version`: Azure DevOps version (default: cloud)
  - `--ado-baseurl`: Azure DevOps Server base URL

#### Export Users Command
- Required:
  - `--ado-org`: Azure DevOps organization name
  - `--ado-pat`: Azure DevOps Personal Access Token
- Optional:
  - `--ado-projects`: Comma-separated list of project names
  - `--output`: Output file path

#### Export SAML Users Command
- Required:
  - `--gh-org`: GitHub organization name
  - `--gh-pat`: GitHub Personal Access Token (requires admin:org scope)

## Security Considerations 🔒

### Required Token Scopes

#### Azure DevOps PAT 🔑
Required scopes for migration:
- Code (read) - For accessing repositories and branches
- Project and Team (read) - For accessing project and team information
- Identity (read) - For user information
- Member Entitlement Management (read) - For team membership details
- Graph (read) - For organizational relationships

The token must have access to all projects being migrated. For TFVC repositories, additional permissions might be required.

#### GitHub PAT 🔑
Required scopes:
- repo - Full control of private repositories, required for:
  - Creating and configuring repositories
  - Setting up branch protection rules
  - Managing repository settings
- workflow - Required for managing GitHub Actions configurations
- admin:org - Required for:
  - Creating and managing teams
  - Setting team permissions
  - Managing organization settings
- delete_repo - Required for repository cleanup or retries

For GitHub Enterprise or organizations with SAML SSO enabled:
- The token must be created by an organization owner or admin
- The token must be authorized for SSO access
- Ensure all required scopes are enabled in SSO authorization

### Best Practices 🛡️
- Store PATs securely and never commit them to source control
- Use minimum required scopes for PATs
- Review GitHub organization security settings before migration
- Backup data before starting migration
- For production migrations, use short-lived PATs
- Rotate PATs after migration is complete

## Contributing 🤝

1. Fork the repository
2. Create your feature branch
3. Commit your changes
4. Push to the branch
5. Create a new Pull Request

## License ⚖️

This project is licensed under the MIT License - see the LICENSE file for details.

## Support 💪

For issues and feature requests, please create an issue in the repository.

## Notes 📝

- Always run assessment before actual migration
- Use dry-run mode for testing
- Backup your data before migration
- Review and validate migration results