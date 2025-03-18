using System.Diagnostics;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Handles the migration of repositories from Azure DevOps to GitHub.
/// Provides functionality to migrate both Git and TFVC repositories with built-in retry mechanisms.
/// </summary>
/// <remarks>
/// This class implements repository migration with the following features:
/// - Support for both Git and TFVC source repositories
/// - Automatic retry mechanism for failed operations
/// - Preservation of repository history and branches
/// - Cleanup of temporary files after migration
/// - Progress logging and error handling
/// </remarks>
public class RepositoryMigrator
{
    /// <summary>
    /// Migrates a repository from Azure DevOps to GitHub with automatic type detection
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source repository in Azure DevOps</param>
    /// <param name="githubOrg">Target GitHub organization name</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <param name="repoName">Name for the new GitHub repository</param>
    /// <param name="projectName">Azure DevOps project name (required for TFVC)</param>
    /// <param name="azureDevOpsPat">Azure DevOps Personal Access Token</param>
    /// <param name="isTfvc">Whether the source is a TFVC repository</param>
    /// <param name="maxRetries">Maximum number of retry attempts for operations</param>
    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    public static async Task<bool> MigrateRepositoryContentAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string repoName,
        string projectName,
        string azureDevOpsPat,
        bool isTfvc = false,
        int maxRetries = 3,
        int retryDelaySeconds = 30)
    {
        return isTfvc 
            ? await MigrateTfvcRepositoryAsync(sourceRepoUrl, githubOrg, githubPat, repoName, projectName, azureDevOpsPat, maxRetries, retryDelaySeconds)
            : await MigrateGitRepositoryAsync(sourceRepoUrl, githubOrg, githubPat, repoName, maxRetries, retryDelaySeconds);
    }

    /// <summary>
    /// Migrates a Git repository from Azure DevOps to GitHub
    /// Uses git clone --mirror to preserve all branches and history
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source Git repository</param>
    /// <param name="githubOrg">Target GitHub organization name</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <param name="repoName">Name for the new GitHub repository</param>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    private static async Task<bool> MigrateGitRepositoryAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string repoName,
        int maxRetries,
        int retryDelaySeconds)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"repo_migration_{repoName}");
        if (Directory.Exists(tempPath))
        {
            Directory.Delete(tempPath, true);
        }

        try
        {
            Logger.LogInfo("Verifying access to Git repository...");

            // Test repository access first
            var testResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"ls-remote {sourceRepoUrl}",
                timeoutSeconds: 30);

            if (!testResult.success)
            {
                throw new Exception("Failed to access repository in Azure DevOps");
            }

            Logger.LogSuccess("Repository is accessible");

            // Clone with mirror option to ensure all refs are copied exactly
            var cloneResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"clone --mirror --progress {sourceRepoUrl} {tempPath}");

            if (!cloneResult.success)
            {
                throw new Exception($"Git clone failed: {cloneResult.error}");
            }

            Directory.SetCurrentDirectory(tempPath);

            // Add GitHub remote and push
            Logger.LogInfo("Setting up GitHub remote...");
            var githubRemoteUrl = $"https://x-access-token:{githubPat}@github.com/{githubOrg}/{repoName}.git";

            var addRemoteInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"remote add github {githubRemoteUrl}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(addRemoteInfo))
            {
                if (process == null || process.ExitCode != 0)
                    throw new Exception("Failed to add GitHub remote");
            }

            // Push to GitHub with retries
            var retryCount = 0;
            var pushSuccess = false;

            while (!pushSuccess && retryCount < maxRetries)
            {
                Logger.LogInfo($"Push retry attempt {retryCount} of {maxRetries} after {retryDelaySeconds} seconds...");
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));

                try
                {
                    Logger.LogInfo("Pushing to GitHub...");
                    var pushInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "push --mirror github",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(pushInfo);
                    if (process == null)
                        throw new Exception("Failed to start git push process");

                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        pushSuccess = true;
                        Logger.LogSuccess("Successfully pushed repository to GitHub");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Push attempt {retryCount + 1} failed: {ex.Message}", ex);
                }

                retryCount++;
            }

            return pushSuccess;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to migrate Git repository: {ex.Message}", ex);
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
            catch
            {
                // Suppress cleanup errors as they shouldn't affect the migration result
            }
        }
    }

    /// <summary>
    /// Migrates a TFVC repository from Azure DevOps to GitHub
    /// Uses git-tfs to convert TFVC to Git while preserving history
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source TFVC repository</param>
    /// <param name="githubOrg">Target GitHub organization name</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <param name="repoName">Name for the new GitHub repository</param>
    /// <param name="projectName">Azure DevOps project name</param>
    /// <param name="azureDevOpsPat">Azure DevOps Personal Access Token</param>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    private static async Task<bool> MigrateTfvcRepositoryAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string repoName,
        string projectName,
        string azureDevOpsPat,
        int maxRetries,
        int retryDelaySeconds)
    {

        // This prevents conflicts when running multiple migrations in parallel
        // Create a unique temporary path for this migration
        // This prevents conflicts when running multiple migrations in parallel
        var tempPath = Path.Combine(Path.GetTempPath(), $"tfvc_migration_{repoName}");
        
        // Ensure clean state by removing any existing temporary directory
        if (Directory.Exists(tempPath))
        {
            Directory.Delete(tempPath, true);
        }

        Directory.CreateDirectory(tempPath);
        Directory.SetCurrentDirectory(tempPath);

        try
        {
            Logger.LogInfo("Initializing git-tfs clone from TFVC...");
            Environment.SetEnvironmentVariable("GIT_TFS_PAT", azureDevOpsPat);

            var retryCount = 0;
            var success = false;

            while (!success && retryCount < maxRetries)
            {
                if (retryCount > 0)
                {
                    Logger.LogWarning($"Retry attempt {retryCount} of {maxRetries} after {retryDelaySeconds} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));

                    // Clean up partial clone
                    foreach (var item in Directory.GetFileSystemEntries(tempPath))
                    {
                        if (Directory.Exists(item))
                            Directory.Delete(item, true);
                        else
                            File.Delete(item);
                    }
                }

                try
                {
                    var result = await ProcessRunner.RunProcessAsync(
                        "git",
                        $"tfs clone {sourceRepoUrl} {tempPath} --branches=all",
                        workingDirectory: tempPath,
                        timeoutSeconds: 3600); // TFVC clones can take longer

                    if (result.success)
                    {
                        success = true;
                    }
                    else if (result.error.Contains("254"))
                    {
                        throw new Exception("git-tfs clone failed - check if the TFVC path is correct");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"git-tfs clone attempt {retryCount + 1} failed: {ex.Message}", ex);
                }

                retryCount++;
            }

            if (!success)
                throw new Exception($"Failed to clone TFVC repository after {maxRetries} attempts");

            // Set up GitHub remote
            Logger.LogInfo("Setting up GitHub remote...");
            var githubUrl = $"https://x-access-token:{githubPat}@github.com/{githubOrg}/{repoName}.git";
            
            var addRemoteResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"remote add github {githubUrl}",
                workingDirectory: tempPath);

            if (!addRemoteResult.success)
            {
                throw new Exception("Failed to add GitHub remote");
            }

            // Push all branches to GitHub
            retryCount = 0;
            var pushSuccess = false;

            while (!pushSuccess && retryCount < maxRetries)
            {
                if (retryCount > 0)
                {
                    Logger.LogWarning($"Push retry attempt {retryCount} of {maxRetries} after {retryDelaySeconds} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
                }

                try
                {
                    // Push all branches
                    var pushResult = await ProcessRunner.RunProcessAsync(
                        "git",
                        "push github --all",
                        workingDirectory: tempPath,
                        timeoutSeconds: 3600); // Large repositories may take time to push

                    if (pushResult.success)
                    {
                        // Push tags if main push succeeded
                        var pushTagsResult = await ProcessRunner.RunProcessAsync(
                            "git",
                            "push github --tags",
                            workingDirectory: tempPath);

                        pushSuccess = pushTagsResult.success;
                        if (pushSuccess)
                        {
                            Logger.LogSuccess("Successfully pushed repository to GitHub");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Push attempt {retryCount + 1} failed: {ex.Message}", ex);
                }

                retryCount++;
            }

            return pushSuccess;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to migrate TFVC repository: {ex.Message}", ex);
            return false;
        }
        finally
        {
            try
            {
                Environment.SetEnvironmentVariable("GIT_TFS_PAT", null);
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}