# Contributing to Azure DevOps to GitHub Migrator

Thank you for your interest in contributing to the Azure DevOps to GitHub Migrator! This document provides guidelines and instructions for contributing to this project.

## Development Prerequisites

- .NET 9.0 SDK or later
- Visual Studio 2022 or later (recommended) or Visual Studio Code
- Git
- Access to Azure DevOps and GitHub for testing

## Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/AzureDevOps2GitHubMigrator.git
   cd AzureDevOps2GitHubMigrator
   ```
3. Create a branch for your work:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Setting Up the Development Environment

1. Open the solution in Visual Studio or VS Code:
   ```bash
   dotnet restore
   dotnet build
   ```

2. Create an `appsettings.Development.json` file for local testing:
   ```json
   {
     "AzureDevOps": {
       "Version": "cloud",
       "BaseUrl": "https://dev.azure.com",
       "Organization": "your-test-org",
       "Pat": "your-test-pat"
     },
     "GitHub": {
       "Organization": "your-test-github-org",
       "Pat": "your-test-github-pat"
     },
     "Migration": {
       "IncludeTeams": true,
       "DryRun": true
     }
   }
   ```

## Development Guidelines

### Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Keep methods focused and concise
- Add XML documentation comments for public APIs
- Use dependency injection where appropriate

### Testing

- Write unit tests for new functionality
- Place tests in the appropriate test class under the `tests` directory
- Follow the existing test naming pattern: `MethodName_Scenario_ExpectedResult`
- Ensure all tests pass before submitting a PR:
  ```bash
  dotnet test
  ```

### Commit Messages

- Use clear and descriptive commit messages
- Start with a verb in the present tense
- Keep the first line under 72 characters
- Reference issue numbers if applicable

Examples:
```
Add support for TFVC repository migration
Fix #123: Handle rate limiting in GitHub API calls
Update documentation for team migration process
```

## Pull Request Process

1. Update documentation if you're changing functionality
2. Add or update unit tests
3. Ensure the test suite passes
4. Update the README.md if needed
5. Submit the PR with a clear description of the changes

### PR Title Format
```
type(scope): description

Types: feat, fix, docs, style, refactor, test, chore
```

Example:
```
feat(migration): add support for TFVC repositories
```

## Running Tests

- Run all tests:
  ```bash
  dotnet test
  ```
- Run specific test category:
  ```bash
  dotnet test --filter "FullyQualifiedName~Services"
  ```

## Debugging

1. Set up test Azure DevOps and GitHub organizations
2. Use the `DryRun` flag in configuration for safe testing
3. Enable debug logging in `appsettings.Development.json`:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Debug"
       }
     }
   }
   ```

## Feature Requests and Bug Reports

- Use GitHub Issues to report bugs or request features
- For bugs, include:
  - Steps to reproduce
  - Expected behavior
  - Actual behavior
  - Environment details
- For feature requests:
  - Clear use case
  - Expected behavior
  - Any potential alternatives considered

## Code Review Process

1. All submissions require review
2. Reviews will look at:
   - Code quality and style
   - Test coverage
   - Documentation
   - Performance implications
   - Security considerations

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

## Questions or Need Help?

- Create an issue for technical questions
- Reference the README.md for basic setup and usage
- Check existing issues and PRs before creating new ones

Thank you for contributing to Azure DevOps to GitHub Migrator!