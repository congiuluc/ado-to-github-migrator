using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using AzureDevOps2GitHubMigrator.Commands;

namespace AzureDevOps2GitHubMigrator;

public class Program
{
    /// <summary>
    /// Entry point of the application
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Azure DevOps to GitHub Migration Tool");
        
        // Add all commands
        rootCommand.AddCommand(CheckGitCommand.Create());
        rootCommand.AddCommand(CheckGitTfsCommand.Create());
        rootCommand.AddCommand(CheckGitHubCliCommand.Create());
        rootCommand.AddCommand(InstallGitCommand.Create());
        rootCommand.AddCommand(InstallGitTfsCommand.Create());
        rootCommand.AddCommand(AdoAssessmentCommand.Create());
        rootCommand.AddCommand(ExportUsersCommand.Create());
        //rootCommand.AddCommand(ExportGithubUsersCommand.Create());
        //rootCommand.AddCommand(UserMappingCommand.Create());
        rootCommand.AddCommand(MigrationCommand.Create());
        rootCommand.AddCommand(MigrationReportCommand.Create());

        // Build and run the parser
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
