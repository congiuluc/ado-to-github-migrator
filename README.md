# Azure DevOps to GitHub Migrator 🚀

A comprehensive tool for migrating repositories, teams, and users from Azure DevOps to GitHub. This tool helps organizations smoothly transition their development workflow while preserving their project structure and team organization.

## Features

### Pre-migration Assessment ✨
- `ado-assessment`: Analyzes Azure DevOps projects and generates a detailed assessment report
- Evaluates repository types (Git/TFVC)
- Identifies potential migration challenges
- Generates JSON report with findings

### Repository Migration 📦
- Supports both Git and TFVC repositories
- Preserves commit history
- Configurable repository naming patterns
- Validates source and target repository status
- Handles large repositories efficiently

### User Management 👥
- `ado-export-users`: Exports Azure DevOps users to CSV
- `gh-export-saml-users`: Exports GitHub SAML users to CSV
- Supports user mapping between platforms
- Preserves user access levels and permissions

### Team Migration 🤝
- Migrates team structures from Azure DevOps to GitHub
- Configurable team naming patterns
- Preserves team memberships
- Supports default team member role configuration

### System Requirements Verification 🔍
- `check-git`: Verifies Git installation
- `check-tfvc`: Verifies Git-TFS installation
- `install-git`: Automated Git installation
- `install-gittfs`: Automated Git-TFS installation

### Reporting 📊
- `migration-report`: Generates detailed migration status reports
- Tracks migration progress
- Identifies successful and failed migrations
- Provides actionable insights for failed migrations

## Installation 💻

1. Ensure you have .NET 9.0 or later installed
2. Clone this repository
3. Build the solution:
   ```bash
   dotnet build src/AzureDevOps2GitHubMigrator.sln
   ```

## Configuration ⚙️

Create an `appsettings.json` file with the following structure:

```json
{
  "AzureDevOps": {
    "Version": "cloud",
    "BaseUrl": "https://dev.azure.com",
    "Organization": "your-org",
    "Pat": "your-ado-pat",
    "DefaultTeamMemberRole": "member"
  },
  "GitHub": {
    "Organization": "your-github-org",
    "Pat": "your-github-pat"
  },
  "Migration": {
    "IncludeTeams": true,
    "RepoNamePattern": "{projectName}-{repoName}",
    "TeamNamePattern": "{projectName}-{teamName}",
    "DryRun": false
  }
}
```

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
Required scopes:
- Code (read)
- Project and Team (read)
- Identity (read)
- Member Entitlement Management (read)
- Graph (read)

Token must have access to all projects being migrated.

#### GitHub PAT 🔑
Required scopes:
- repo (full control of private repositories)
- workflow (manage GitHub Actions)
- admin:org (manage organization settings)
- delete_repo (delete repositories)

Token must be created by an organization owner or admin with sufficient permissions.
For organizations with SAML SSO, the token must be authorized for SSO access.

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