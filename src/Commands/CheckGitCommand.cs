using System.CommandLine;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Commands;

public class CheckGitCommand
{
    public static Command Create()
    {
        var command = new Command("check-git", "Check if Git is installed");

        command.SetHandler(async () =>
        {
            try
            {
                if (await RequiredModulesChecker.VerifyGitAsync())
                    Logger.LogSuccess("Git is installed and configured correctly.");
                else
                    Logger.LogError("Git installation check failed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Git check failed: {ex.Message}", ex);
            }
        });

        return command;
    }
}

