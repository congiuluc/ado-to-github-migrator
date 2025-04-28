using AzureDevOps2GitHubMigrator.GitHub;
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
    private static readonly GitHubService _githubService;

    static RepositoryMigrator()
    {
        _githubService = new GitHubService(new HttpClient());
    }

    /// <summary>
    /// Migrates a repository from Azure DevOps to GitHub with automatic type detection
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source repository in Azure DevOps</param>
    /// <param name="githubOrg">Target GitHub organization name</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <param name="repoName">Name for the new GitHub repository</param>
    /// <param name="projectName">Azure DevOps project name (required for TFVC)</param>
    /// <param name="azureDevOpsPat">Azure DevOps Personal Access Token</param>
    /// <param name="workingDir">Working directory for the migration process</param>
    /// <param name="isTfvc">Whether the source is a TFVC repository</param>
    /// <param name="maxRetries">Maximum number of retry attempts for operations</param>
    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    public static async Task<bool> MigrateRepositoryContentAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string projectName,
        string repoName,
        string mainBranchName,
        string azureDevOpsBaseUrl,
        string azureDevOpsPat,
        string workingDir,
        bool isTfvc = false,
        bool gitDisableSslVerify = false,
        bool gitUsePatForClone = false, // New parameter
        int maxRetries = 3,
        int retryDelaySeconds = 30)
    {
        return isTfvc
            ? await MigrateTfvcRepositoryAsync(sourceRepoUrl, githubOrg, githubPat, projectName, repoName, mainBranchName, azureDevOpsBaseUrl, azureDevOpsPat, maxRetries, retryDelaySeconds, workingDir)
            : await MigrateGitRepositoryAsync(sourceRepoUrl, githubOrg, githubPat, projectName, repoName, mainBranchName, azureDevOpsBaseUrl, azureDevOpsPat, maxRetries, retryDelaySeconds, workingDir, gitDisableSslVerify, gitUsePatForClone);
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
    /// <param name="workingDir">Working directory for the migration process</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    private static async Task<bool> MigrateGitRepositoryAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string projectName,
        string repoName,
        string mainBranchName,
        string azureDevOpsBaseUrl,
        string azureDevOpsPat,
        int maxRetries,
        int retryDelaySeconds,
        string workingDir,
        bool gitDisableSslVerify = true,
        bool gitUsePatForClone = false) // New parameter
    {
        // Ensure working directory exists
        if (!Directory.Exists(workingDir))
        {
            try
            {
                Directory.CreateDirectory(workingDir);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create working directory {workingDir}: {ex.Message}");
                return false;
            }
        }

        var tempPath = Path.Combine(workingDir, $"migration_{projectName}_{repoName}_{DateTime.Now:yyyyMMdd}");

        // Use the new safe delete method
        await Common.SafeDeleteDirectoryAsync(tempPath);

        try
        {
            Logger.LogInfo("Verifying access to Git repository...");

            // Add authentication to source URL if usePatForClone is true
            var authenticatedSourceUrl = gitUsePatForClone
                ? sourceRepoUrl.Replace("://", $"://{azureDevOpsPat}@")
                : sourceRepoUrl;
            //var authenticatedSourceUrl = sourceRepoUrl;
            // Test repository access first with SSL verification disabled for this command only
            var testResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"-c http.sslVerify={(gitDisableSslVerify ? "false" : "true")} ls-remote {authenticatedSourceUrl}",
                timeoutSeconds: 30);

            if (!testResult.success)
            {
                throw new Exception($"Failed to access repository in Azure DevOps: {testResult.error}");
            }

            Logger.LogSuccess("Repository is accessible");
            Logger.LogInfo("Cloning repository...");

            // Clone with mirror option to ensure all refs are copied exactly
            var cloneResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"-c http.sslVerify={(gitDisableSslVerify ? "false" : "true")} clone --mirror --progress {authenticatedSourceUrl} {tempPath}");

            if (!cloneResult.success)
            {
                throw new Exception($"Git clone failed: {cloneResult.error}");
            }

            Directory.SetCurrentDirectory(tempPath);

            // Set HEAD to point to the desired default branch
            Logger.LogInfo($"Setting HEAD symbolic reference to {mainBranchName}...");
            var setHeadResult = await ProcessRunner.RunProcessAsync(
                "git",
                $"symbolic-ref HEAD refs/heads/{mainBranchName}",
                workingDirectory: tempPath);

            if (!setHeadResult.success)
            {
                Logger.LogWarning($"Failed to set HEAD symbolic reference: {setHeadResult.error}");
            }
            else
            {
                Logger.LogSuccess($"HEAD symbolic reference set to {mainBranchName}");
            }


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

            using var process = Process.Start(addRemoteInfo);
            if (process == null)
                throw new Exception("Failed to start git process");

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new Exception("Failed to add GitHub remote");

            // Configure git post buffer size for large pushes
            var configResult = await ProcessRunner.RunProcessAsync(
                "git",
                "config http.postBuffer 524288000",
                workingDirectory: tempPath);

            if (!configResult.success)
            {
                Logger.LogWarning("Failed to set git post buffer size. Large pushes may fail.");
            }

            // Push to GitHub with retries
            var retryCount = 0;
            var pushSuccess = false;

            while (!pushSuccess && retryCount < maxRetries)
            {
                if (retryCount > 0)
                {
                    Logger.LogInfo($"Push retry attempt {retryCount} of {maxRetries} after {retryDelaySeconds} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
                }

                try
                {
                    Logger.LogInfo("Pushing to GitHub...");
                    
                    // First, configure git to exclude pull request references
                    var configPushResult = await ProcessRunner.RunProcessAsync(
                        "git",
                        "config --local --unset-all remote.github.mirror",
                        workingDirectory: tempPath);
                    
                    // Use a direct push command that specifies the refs explicitly instead of relying on refspecs
                    var pushResult = await ProcessRunner.RunProcessAsync(
                        "git",
                        "push github --all",  // Push all branches
                        workingDirectory: tempPath,
                        timeoutSeconds: 3600); // Large repositories may take time to push
                        
                    // Also push all tags separately
                    if (pushResult.success)
                    {
                        var pushTagsResult = await ProcessRunner.RunProcessAsync(
                            "git",
                            "push github --tags",  // Push all tags
                            workingDirectory: tempPath,
                            timeoutSeconds: 1200);
                            
                        pushSuccess = pushTagsResult.success;
                    }
                    else
                    {
                        pushSuccess = false;
                    }

                    if (pushSuccess)
                    {
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
                Directory.SetCurrentDirectory(workingDir);
                await Common.SafeDeleteDirectoryAsync(tempPath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to clean up temporary directory: {ex.Message}");
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
    /// <param name="workingDir">Working directory for the migration process</param>
    /// <returns>True if migration was successful, false otherwise</returns>
    private static async Task<bool> MigrateTfvcRepositoryAsync(
        string sourceRepoUrl,
        string githubOrg,
        string githubPat,
        string projectName,
        string repoName,
        string mainBranchName,
        string azureDevOpsBaseUrl,
        string azureDevOpsPat,
        int maxRetries,
        int retryDelaySeconds,
        string workingDir) 
    {
        var tempPath = Path.Combine(workingDir, $"tfvc_migration_{repoName}");

        sourceRepoUrl = azureDevOpsBaseUrl;

        // Use the new safe delete method
        await Common.SafeDeleteDirectoryAsync(tempPath);
        Directory.CreateDirectory(tempPath);
        Directory.SetCurrentDirectory(tempPath);

        try
        {
            Logger.LogInfo("Initializing git-tfs clone from TFVC...");
            Environment.SetEnvironmentVariable("GIT_TFS_PAT", azureDevOpsPat, EnvironmentVariableTarget.Process);


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
                    // Add authentication to source URL if usePatForClone is true
                 
                    var result = await ProcessRunner.RunProcessAsync(
                        "git",
                        $"tfs clone {sourceRepoUrl} {mainBranchName} {tempPath} --branches=all",
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
                        "push --mirror github",  // Changed to use --mirror to preserve all refs exactly
                        workingDirectory: tempPath,
                        timeoutSeconds: 3600); // Large repositories may take time to push

                    if (pushResult.success)
                    {
                        pushSuccess = true;
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
                Directory.SetCurrentDirectory(workingDir);
                await Common.SafeDeleteDirectoryAsync(tempPath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to clean up temporary directory: {ex.Message}");
            }
        }
    }
}