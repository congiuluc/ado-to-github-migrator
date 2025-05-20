using AzureDevOps2GitHubMigrator.GitHub;
using AzureDevOps2GitHubMigrator.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

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
    /// Checks if a GitHub repository exists in the specified organization
    /// </summary>
    /// <param name="githubOrg">GitHub organization name</param>
    /// <param name="repoName">Repository name to check</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <returns>True if the repository exists, false otherwise</returns>
    private static async Task<bool> RepositoryExistsAsync(string githubOrg, string repoName, string githubPat)
    {
        try
        {
            // Use GitHubService to check if the repository exists
            if (!string.IsNullOrEmpty(githubPat) && githubPat != _githubService.GetPat())
            {
                _githubService.UpdatePat(githubPat);
            }
            
            return await _githubService.RepositoryExistsAsync(githubOrg, repoName);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error checking if repository exists: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if a GitHub repository has no commits (is empty)
    /// </summary>
    /// <param name="githubOrg">GitHub organization name</param>
    /// <param name="repoName">Repository name to check</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <returns>True if the repository is empty, false if it has commits</returns>
    private static async Task<bool> IsRepositoryEmptyAsync(string githubOrg, string repoName, string githubPat)
    {
        try
        {
            // Use GitHubService to check if the repository is empty
            if (!string.IsNullOrEmpty(githubPat) && githubPat != _githubService.GetPat())
            {
                _githubService.UpdatePat(githubPat);
            }
            
            // Call the service method that handles the API request
            return await _githubService.IsRepositoryEmptyAsync(githubOrg, repoName);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error checking if repository is empty: {ex.Message}");
            // If there's an error, assume it's not empty to be safe
            return false;
        }
    }

    /// <summary>
    /// Gets the latest commit hash from a GitHub repository
    /// </summary>
    /// <param name="githubOrg">GitHub organization name</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="branchName">Branch name to check</param>
    /// <param name="githubPat">GitHub Personal Access Token</param>
    /// <returns>Latest commit hash or null if not found</returns>
    private static async Task<string?> GetGitHubLatestCommitAsync(string githubOrg, string repoName, string branchName, string githubPat)
    {
        try
        {
            // Use GitHubService to get the latest commit for a branch
            if (!string.IsNullOrEmpty(githubPat) && githubPat != _githubService.GetPat())
            {
                _githubService.UpdatePat(githubPat);
            }
            
            // Call the service method that handles the API request
            return await _githubService.GetBranchLatestCommitAsync(githubOrg, repoName, branchName);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error getting latest GitHub commit: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the latest commit hash from an Azure DevOps Git repository
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source Git repository</param>
    /// <param name="branchName">Branch name to check</param>
    /// <param name="azureDevOpsPat">Azure DevOps Personal Access Token</param>
    /// <param name="gitUsePatForClone">Whether to use PAT for authentication</param>
    /// <param name="gitDisableSslVerify">Whether to disable SSL verification</param>
    /// <returns>Latest commit hash or null if not found</returns>
    private static async Task<string?> GetAdoGitLatestCommitAsync(string sourceRepoUrl, string branchName, string azureDevOpsPat, bool gitUsePatForClone = false, bool gitDisableSslVerify = false)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"ado_commit_check_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var authenticatedSourceUrl = gitUsePatForClone
                    ? sourceRepoUrl.Replace("://", $"://{azureDevOpsPat}@")
                    : sourceRepoUrl;
                
                var lsRemoteResult = await ProcessRunner.RunProcessAsync(
                    "git",
                    $"-c http.sslVerify={(gitDisableSslVerify ? "false" : "true")} ls-remote {authenticatedSourceUrl} refs/heads/{branchName}",
                    workingDirectory: tempDir,
                    timeoutSeconds: 30);

                if (!lsRemoteResult.success)
                {
                    Logger.LogWarning($"Failed to get latest commit from Azure DevOps: {lsRemoteResult.error}");
                    return null;
                }

                var output = lsRemoteResult.output.Trim();
                if (string.IsNullOrEmpty(output))
                {
                    return null;
                }

                // The format is: <commit-hash>\trefs/heads/<branch-name>
                var commitHash = output.Split('\t')[0].Trim();
                return commitHash;
            }
            finally
            {
                await Common.SafeDeleteDirectoryAsync(tempDir);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error getting latest Azure DevOps Git commit: {ex.Message}");
            return null;
        }
    }    /// <summary>
    /// Gets the latest local Git commit hash after performing git-tfs conversion
    /// for a TFVC repository
    /// </summary>
    /// <param name="azureDevOpsBaseUrl">Azure DevOps base URL</param>
    /// <param name="azureDevOpsPat">Azure DevOps Personal Access Token</param>
    /// <param name="branch">The branch to check</param>
    /// <returns>Latest Git commit hash or null if not found</returns>
    private static async Task<string?> GetAdoTfvcLatestChangesetAsync(
        string azureDevOpsBaseUrl,
        string azureDevOpsPat,
        string? branch = null)
    {
        try
        {
            // Create a temporary directory for git-tfs clone
            var tempDir = Path.Combine(Path.GetTempPath(), $"tfvc_commit_check_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                Logger.LogInfo("Checking latest TFVC commit by doing a minimal git-tfs clone...");
                Environment.SetEnvironmentVariable("GIT_TFS_PAT", azureDevOpsPat, EnvironmentVariableTarget.Process);
                
                // Run git-tfs quick-clone to just get the latest commit (with depth=1)
                var cloneResult = await ProcessRunner.RunProcessAsync(
                    "git",
                    $"tfs quick-clone {azureDevOpsBaseUrl} {branch ?? "$/"}",
                    workingDirectory: tempDir,
                    timeoutSeconds: 300); // Allow up to 5 minutes for the clone
                
                if (!cloneResult.success)
                {
                    Logger.LogWarning($"Failed to perform quick git-tfs clone: {cloneResult.error}");
                    return null;
                }
                
                // Get the latest commit hash
                var gitDir = Directory.GetDirectories(tempDir).FirstOrDefault();
                if (gitDir == null)
                {
                    Logger.LogWarning("No git directory found after git-tfs quick-clone");
                    return null;
                }
                
                var commitResult = await ProcessRunner.RunProcessAsync(
                    "git", 
                    "log -1 --format=%H",
                    workingDirectory: gitDir,
                    timeoutSeconds: 30);
                
                if (commitResult.success && !string.IsNullOrEmpty(commitResult.output))
                {
                    var commitHash = commitResult.output.Trim();
                    Logger.LogInfo($"Latest TFVC commit hash: {commitHash}");
                    return commitHash;
                }
                
                Logger.LogWarning("Could not retrieve latest commit hash from TFVC repository");
                return null;
            }
            finally
            {
                Environment.SetEnvironmentVariable("GIT_TFS_PAT", null);
                await Common.SafeDeleteDirectoryAsync(tempDir);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error getting latest TFVC commit: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Verifies if the main branch commits match between source and target repositories
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source repository</param>
    /// <param name="githubOrg">GitHub organization name</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="mainBranchName">Name of the main branch</param>
    /// <param name="azureDevOpsBaseUrl">Azure DevOps base URL (used for TFVC)</param>
    /// <param name="azureDevOpsPat">Azure DevOps PAT</param>
    /// <param name="githubPat">GitHub PAT</param>
    /// <param name="isTfvc">Whether the source is a TFVC repository</param>
    /// <param name="gitUsePatForClone">Whether to use PAT for Git clone</param>
    /// <param name="gitDisableSslVerify">Whether to disable SSL verification</param>
    /// <returns>A tuple with (success, commitInfo) where commitInfo contains source and target commit hashes</returns>
    private static async Task<(bool success, string sourceCommit, string targetCommit)> VerifyMainBranchCommitAsync(
        string sourceRepoUrl,
        string githubOrg,
        string repoName,
        string mainBranchName,
        string azureDevOpsBaseUrl,
        string azureDevOpsPat,
        string githubPat,
        bool isTfvc = false,
        bool gitUsePatForClone = false,
        bool gitDisableSslVerify = false)
    {
        // Get latest commit from GitHub
        var githubLatestCommit = await GetGitHubLatestCommitAsync(githubOrg, repoName, mainBranchName, githubPat);
        
        // Get latest commit from source repository
        string? sourceLatestCommit;
        if (!isTfvc)
        {
            sourceLatestCommit = await GetAdoGitLatestCommitAsync(sourceRepoUrl, mainBranchName, azureDevOpsPat, gitUsePatForClone, gitDisableSslVerify);
        }
        else
        {
            sourceLatestCommit = await GetAdoTfvcLatestChangesetAsync(azureDevOpsBaseUrl, azureDevOpsPat, mainBranchName);
        }
        
        // Check if we could get both commits
        if (string.IsNullOrEmpty(githubLatestCommit) || string.IsNullOrEmpty(sourceLatestCommit))
        {
            return (false, sourceLatestCommit ?? "unknown", githubLatestCommit ?? "unknown");
        }
        
        // Check if commits match
        return (githubLatestCommit == sourceLatestCommit, sourceLatestCommit, githubLatestCommit);
    }

    /// <summary>
    /// Prompts the user for confirmation to continue with repository synchronization
    /// </summary>
    /// <param name="repoName">Repository name</param>
    /// <param name="sourceCommit">Latest commit hash from source repository</param>
    /// <param name="targetCommit">Latest commit hash from target repository</param>
    /// <returns>True if the user confirms, false otherwise</returns>
    private static bool ConfirmRepositorySync(string repoName, string sourceCommit, string targetCommit)
    {
        Logger.LogWarning($"Repository '{repoName}' already exists in GitHub with different commits.");
        Logger.LogInfo($"Source latest commit: {sourceCommit}");
        Logger.LogInfo($"Target latest commit: {targetCommit}");
        Console.Write("Do you want to synchronize the repository? This may overwrite changes in GitHub. (y/N): ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
        return response == "y" || response == "yes";
    }

    /// <summary>
    /// Performs a thorough comparison of source and target Git repositories to ensure migration was complete
    /// Compares branches and commit counts
    /// </summary>
    /// <param name="sourceRepoUrl">URL of the source Git repository</param>
    /// <param name="targetRepoUrl">URL of the target GitHub repository</param>
    /// <param name="workingDir">Working directory for temporary operations</param>
    /// <param name="azureDevOpsPat">Azure DevOps PAT for authentication</param>
    /// <param name="githubPat">GitHub PAT for authentication</param>
    /// <param name="gitDisableSslVerify">Whether to disable SSL verification</param>
    /// <param name="gitUsePatForClone">Whether to use PAT for cloning</param>
    /// <returns>A tuple with (success, mismatchReason)</returns>
    private static async Task<(bool success, string mismatchReason)> VerifyGitMigrationIntegrityAsync(
        string sourceRepoUrl,
        string githubOrg,
        string repoName,
        string workingDir,
        string azureDevOpsPat,
        string githubPat,
        bool gitDisableSslVerify = false,
        bool gitUsePatForClone = false)
    {
        var assessmentTempDir = Path.Combine(workingDir, $"migration_assessment_{Guid.NewGuid()}");
        Directory.CreateDirectory(assessmentTempDir);
        
        try
        {
            Logger.LogInfo("Starting comprehensive migration assessment...");
            
            // Prepare repository URLs with authentication if needed
            var authenticatedSourceUrl = gitUsePatForClone
                ? sourceRepoUrl.Replace("://", $"://{azureDevOpsPat}@")
                : sourceRepoUrl;
                
            var targetRepoUrl = $"https://x-access-token:{githubPat}@github.com/{githubOrg}/{repoName}.git";
            
            // Create temp directories for assessment
            var sourceTempDir = Path.Combine(assessmentTempDir, "source");
            var targetTempDir = Path.Combine(assessmentTempDir, "target");
            Directory.CreateDirectory(sourceTempDir);
            Directory.CreateDirectory(targetTempDir);
            
            // Clone source repository with bare option (metadata only, no files)
            Logger.LogInfo("Cloning source repository for assessment...");
            var sourceCloneResult = await ProcessRunner.RunProcessAsync(
                "git", 
                $"-c http.sslVerify={(gitDisableSslVerify ? "false" : "true")} clone --bare {authenticatedSourceUrl} {sourceTempDir}",
                timeoutSeconds: 600);
                
            if (!sourceCloneResult.success)
            {
                return (false, $"Failed to clone source repository: {sourceCloneResult.error}");
            }
            
            // Clone target repository with bare option
            Logger.LogInfo("Cloning target repository for assessment...");
            var targetCloneResult = await ProcessRunner.RunProcessAsync(
                "git", 
                $"clone --bare {targetRepoUrl} {targetTempDir}",
                timeoutSeconds: 600);
                
            if (!targetCloneResult.success)
            {
                return (false, $"Failed to clone target repository: {targetCloneResult.error}");
            }
            
            // Compare branch counts
            var sourceBranchesResult = await ProcessRunner.RunProcessAsync(
                "git", 
                "branch",
                workingDirectory: sourceTempDir);
                
            var targetBranchesResult = await ProcessRunner.RunProcessAsync(
                "git", 
                "branch",
                workingDirectory: targetTempDir);
                
            var sourceBranchCount = sourceBranchesResult.success ? 
                sourceBranchesResult.output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
                
            var targetBranchCount = targetBranchesResult.success ? 
                targetBranchesResult.output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
            
            Logger.LogInfo($"Source repository has {sourceBranchCount} branches");
            Logger.LogInfo($"Target repository has {targetBranchCount} branches");
            
            if (sourceBranchCount != targetBranchCount)
            {
                return (false, $"Branch count mismatch: source has {sourceBranchCount} branches, target has {targetBranchCount} branches");
            }
            
            // Compare commit counts
            var sourceCommitCountResult = await ProcessRunner.RunProcessAsync(
                "git", 
                "rev-list --all --count",
                workingDirectory: sourceTempDir);
                
            var targetCommitCountResult = await ProcessRunner.RunProcessAsync(
                "git", 
                "rev-list --all --count",
                workingDirectory: targetTempDir);
                
            if (!sourceCommitCountResult.success || !targetCommitCountResult.success)
            {
                return (false, "Failed to get commit counts for comparison");
            }
            
            var sourceCommitCount = int.Parse(sourceCommitCountResult.output.Trim());
            var targetCommitCount = int.Parse(targetCommitCountResult.output.Trim());
            
            Logger.LogInfo($"Source repository has {sourceCommitCount} commits");
            Logger.LogInfo($"Target repository has {targetCommitCount} commits");
            
            // Some slight difference in commit count might be acceptable due to GitHub specific changes
            // Use a small tolerance threshold (e.g., 5% difference)
            var threshold = Math.Max(1, sourceCommitCount * 0.05);
            var difference = Math.Abs(sourceCommitCount - targetCommitCount);
              if (difference > threshold)
            {
                return (false, $"Commit count mismatch: source has {sourceCommitCount} commits, target has {targetCommitCount} commits");
            }
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Error during migration assessment: {ex.Message}");
        }
        finally
        {
            // Clean up temporary directories
            await Common.SafeDeleteDirectoryAsync(assessmentTempDir);
        }
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
    /// <param name="maxRetries">Maximum number of retry attempts for operations</param>    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <param name="forceSync">Force synchronization without prompting (optional)</param>
    /// <returns>Migration status indicating whether the repository was fully migrated, partially migrated, or failed</returns>
    public static async Task<MigrationStatus> MigrateRepositoryContentAsync(
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
        bool gitUsePatForClone = false,
        int maxRetries = 3,
        int retryDelaySeconds = 30,
        bool forceSync = false)
    {
        // First, check if the repository already exists in GitHub
        Logger.LogInfo($"Checking if repository '{repoName}' exists in GitHub organization '{githubOrg}'...");
        var repoExists = await RepositoryExistsAsync(githubOrg, repoName, githubPat);        if (repoExists)
        {
            Logger.LogInfo($"Repository '{repoName}' already exists in GitHub.");
            
            // Check if the repository is empty
            var isEmpty = await IsRepositoryEmptyAsync(githubOrg, repoName, githubPat);
            if (isEmpty)
            {
                Logger.LogInfo($"Repository '{repoName}' exists in GitHub but is empty. Proceeding with migration without commit comparison...");
            }
            // Only do commit comparison if repository is not empty and forceSync is not enabled
            else if (!forceSync)
            {
                // Get the latest commit from GitHub repository
                var githubLatestCommit = await GetGitHubLatestCommitAsync(githubOrg, repoName, mainBranchName, githubPat);
                // Get the latest commit from Azure DevOps repository
                string? adoLatestCommit;
                if (!isTfvc)
                {
                    adoLatestCommit = await GetAdoGitLatestCommitAsync(sourceRepoUrl, mainBranchName, azureDevOpsPat, gitUsePatForClone, gitDisableSslVerify);
                }
                else
                {
                    adoLatestCommit = await GetAdoTfvcLatestChangesetAsync(azureDevOpsBaseUrl, azureDevOpsPat, mainBranchName);
                }
                
                // If we couldn't get either commit, log a warning and proceed with migration
                if (string.IsNullOrEmpty(githubLatestCommit) || string.IsNullOrEmpty(adoLatestCommit))
                {
                    Logger.LogWarning("Could not compare latest commits. Will prompt for confirmation.");
                }                else if (!string.IsNullOrEmpty(githubLatestCommit) && githubLatestCommit == adoLatestCommit)
                {
                    // Repositories are already in sync, skip migration
                    Logger.LogSuccess($"Repository '{repoName}' is already in sync. Skipping migration.");
                    return MigrationStatus.Completed;
                }
                
                // If commits are different, ask for confirmation
                string sourceCommit = adoLatestCommit ?? "unknown";
                string targetCommit = githubLatestCommit ?? "unknown";
                if (!ConfirmRepositorySync(repoName, sourceCommit, targetCommit))
                {
                    Logger.LogInfo("Migration cancelled by user.");
                    return MigrationStatus.Skipped;
                }
                
                Logger.LogInfo("Proceeding with repository synchronization...");
            }            else
            {
                Logger.LogInfo("Force sync enabled. Proceeding with repository synchronization without commit comparison...");
            }
        }
        else
        {
            Logger.LogInfo($"Repository '{repoName}' does not exist in GitHub. Proceeding with migration...");
        }
          // Continue with the original migration logic
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
    /// <param name="repoName">Name for the new GitHub repository</param>    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <param name="workingDir">Working directory for the migration process</param>
    /// <returns>MigrationStatus indicating whether the repository was fully migrated, partially migrated, or failed</returns>
    private static async Task<MigrationStatus> MigrateGitRepositoryAsync(
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
        bool gitUsePatForClone = false) 
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
                return MigrationStatus.Failed;
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

                        // Set default branch using GitHub API through GitHubService
                        Logger.LogDebug($"Setting default branch to {mainBranchName}...");
                        
                        try
                        {
                            // Use the existing UpdateDefaultBranchAsync method directly
                            if (!string.IsNullOrEmpty(githubPat) && githubPat != _githubService.GetPat())
                            {
                                _githubService.UpdatePat(githubPat);
                            }
                            
                            await _githubService.UpdateDefaultBranchAsync(githubOrg, repoName, mainBranchName);                            Logger.LogSuccess($"Successfully set default branch to {mainBranchName}");
                              Logger.LogSuccess("Repository was migrated successfully.");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to set default branch: {ex.Message}");
                            Logger.LogWarning("Repository was migrated successfully, but default branch may need manual configuration.");
                        }                        // Verify if source and target commits match after push
                        Logger.LogInfo("Verifying repository migration status by comparing commits...");
                        
                        // Check main branch synchronization
                        var (mainBranchSynced, sourceCommit, targetCommit) = await VerifyMainBranchCommitAsync(
                            sourceRepoUrl, githubOrg, repoName, mainBranchName, 
                            azureDevOpsBaseUrl, azureDevOpsPat, githubPat, 
                            false, gitUsePatForClone, gitDisableSslVerify);
                            
                        // Basic verification of main branch head commit
                        bool basicVerificationPassed = false;
                        if (sourceCommit == "unknown" || targetCommit == "unknown")
                        {
                            Logger.LogWarning("Could not verify main branch commit hashes. Will perform comprehensive assessment instead.");
                        }
                        else if (mainBranchSynced)
                        {
                            Logger.LogSuccess("Main branch commit hashes match.");
                            basicVerificationPassed = true;
                        }
                        else
                        {
                            Logger.LogWarning($"Main branch commits don't match:");
                            Logger.LogWarning($"  Source: {sourceCommit}");
                            Logger.LogWarning($"  Target: {targetCommit}");
                        }
                        
                        // Perform comprehensive repository assessment
                        Logger.LogInfo("Performing comprehensive repository assessment...");
                        var (integrityVerified, mismatchReason) = await VerifyGitMigrationIntegrityAsync(
                            sourceRepoUrl, githubOrg, repoName, workingDir, 
                            azureDevOpsPat, githubPat, gitDisableSslVerify, gitUsePatForClone);
                            
                        if (integrityVerified)
                        {
                            Logger.LogSuccess("Repository integrity verification passed. All branches and commits verified.");
                            return MigrationStatus.Completed;
                        }
                        else
                        {
                            Logger.LogWarning($"Repository integrity verification found issues: {mismatchReason}");
                            
                            // If basic verification passed but comprehensive check failed, still report partially completed
                            if (basicVerificationPassed)
                            {
                                Logger.LogInfo("Main branch is correctly migrated, but there may be issues with other branches.");
                            }
                            
                            return MigrationStatus.PartiallyCompleted;
                        }
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError($"Push attempt {retryCount + 1} failed: {ex.Message}", ex);
                }

                retryCount++;
            }            return pushSuccess ? MigrationStatus.PartiallyCompleted : MigrationStatus.Failed;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to migrate Git repository: {ex.Message}", ex);
            return MigrationStatus.Failed;
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
    /// <param name="maxRetries">Maximum number of retry attempts</param>    /// <param name="retryDelaySeconds">Delay between retry attempts in seconds</param>
    /// <param name="workingDir">Working directory for the migration process</param>
    /// <returns>MigrationStatus indicating whether the repository was fully migrated, partially migrated, or failed</returns>
    private static async Task<MigrationStatus> MigrateTfvcRepositoryAsync(
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
            }            if (pushSuccess)
            {                // Verify if source and target commits match after push
                Logger.LogInfo("Verifying TFVC repository migration status by comparing commits...");
                
                // Check main branch synchronization using the shared verification method
                var (mainBranchSynced, sourceCommit, targetCommit) = await VerifyMainBranchCommitAsync(
                    sourceRepoUrl, githubOrg, repoName, mainBranchName, 
                    azureDevOpsBaseUrl, azureDevOpsPat, githubPat, 
                    true, false, false);
                    
                if (sourceCommit == "unknown" || targetCommit == "unknown")
                {
                    Logger.LogWarning("Could not verify commit hashes after TFVC migration. Marking repository as partially migrated.");
                    return MigrationStatus.PartiallyCompleted;
                }
                
                if (mainBranchSynced)
                {
                    // For TFVC repositories, we can't use the same comprehensive check as with Git repos
                    // since the source is not a Git repository. Matching main branch commit is sufficient.
                    Logger.LogSuccess("TFVC repository was migrated successfully. Commit hashes match.");
                    return MigrationStatus.Completed;
                }
                else
                {
                    Logger.LogWarning($"TFVC repository was migrated, but the latest commits don't match:");
                    Logger.LogWarning($"  Source: {sourceCommit}");
                    Logger.LogWarning($"  Target: {targetCommit}");
                    return MigrationStatus.PartiallyCompleted;
                }
            }
            
            return pushSuccess ? MigrationStatus.PartiallyCompleted : MigrationStatus.Failed;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to migrate TFVC repository: {ex.Message}", ex);
            return MigrationStatus.Failed;
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
