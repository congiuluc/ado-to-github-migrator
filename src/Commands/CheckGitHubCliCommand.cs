using System.CommandLine;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Commands
{
    public class CheckGitHubCliCommand
    {
        /// <summary>
        /// Creates a command to check if GitHub CLI is installed and working correctly
        /// </summary>
        /// <returns>A Command instance for checking GitHub CLI</returns>
        public static Command Create()
        {
            var command = new Command(
                "check-gh", 
                "Check if GitHub CLI is installed and working correctly"
            );

            command.SetHandler(async () => 
            {
                Logger.LogInfo("Checking GitHub CLI installation...");
                bool isInstalled = await RequiredModulesChecker.VerifyGitHubCliAsync();

                if (isInstalled)
                {
                    Logger.LogSuccess("GitHub CLI is installed and working correctly");
                }
                else
                {
                    Logger.LogError("GitHub CLI is not installed or not working correctly");
                    Logger.LogInfo("Please follow the installation instructions at https://cli.github.com/");
                }
            });

            return command;
        }
    }
}