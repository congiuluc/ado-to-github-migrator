using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Utils;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class InstallGitCommand
{
    public static Command Create()
    {
        var command = new Command("install-git", "Install Git");

        command.SetHandler(async (InvocationContext context) =>
        {
            try
            {
                if (await GitInstaller.InstallGitAsync())
                    Logger.LogSuccess("Git installation completed successfully.");
                else
                    Logger.LogError("Git installation failed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Git installation failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}