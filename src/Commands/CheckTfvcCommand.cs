using System.CommandLine;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Commands;

public class CheckGitTfsCommand
{
 
    public static Command Create()
    {
        var command = new Command("check-git-tfs", "Check if Git-TFS is installed");

      
        command.SetHandler(async () =>
        {
            try
            {
                if (await GitTfsInstaller.VerifyGitTfsInstallationAsync())
                    Logger.LogSuccess("Git-TFS is installed and configured correctly.");
                else
                    Logger.LogError("Git-TFS installation check failed.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Git-TFS check failed: {ex.Message}", ex);
            }
        });

        return command;
    }


}