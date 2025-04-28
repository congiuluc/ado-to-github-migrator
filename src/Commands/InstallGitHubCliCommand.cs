using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Utils;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class InstallGitHubCliCommand
{
    public static Command Create()
    {
        var command = new Command("install-gh", "Install GitHub CLI");

        command.SetHandler(async (InvocationContext context) =>
        {
            try
            {
                if (await GitHubCliInstaller.InstallGitHubCliAsync())
                    Logger.LogSuccess("GitHub CLI installation completed successfully.");
                else
                    Logger.LogError("GitHub CLI installation failed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"GitHub CLI installation failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}