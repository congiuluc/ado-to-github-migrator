using System.CommandLine;
using System.CommandLine.Invocation;
using AzureDevOps2GitHubMigrator.Utils;
using Microsoft.Extensions.Configuration;

namespace AzureDevOps2GitHubMigrator.Commands;

public class InstallGitTfsCommand
{
    public static Command Create()
    {
        var command = new Command("install-git-tfs", "Install Git-TFS");

        var skipChocolateyOption = new Option<bool>("--skip-chocolatey", () => false, "Skip Chocolatey installation and try direct download");

        command.AddOption(skipChocolateyOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var skipChocolatey = context.ParseResult.GetValueForOption(skipChocolateyOption);

            try
            {
                if (await GitTfsInstaller.InstallGitTfsAsync(skipChocolatey))
                    Logger.LogSuccess("Git-TFS installation completed successfully.");
                else
                    Logger.LogError("Git-TFS installation failed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Git-TFS installation failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}