using System.Diagnostics;
using AzureDevOps2GitHubMigrator.Utils;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Validates and ensures required external tools and modules are available for the migration process.
/// </summary>
/// <remarks>
/// This class is responsible for:
/// - Checking Git and git-tfs installations.
/// - Checking GitHub CLI installation.
/// - Dependency validation before migration.
/// - Automated installation of missing components.
/// - Version compatibility checks.
/// </remarks>
public static class RequiredModulesChecker
{
    /// <summary>
    /// Ensures that all required modules for migration are available.
    /// </summary>
    /// <param name="requireGitTfs">Whether git-tfs is required for this migration</param>
    /// <returns>True if all required modules are available, false otherwise</returns>
    public static async Task<bool> EnsureRequiredModulesAsync(bool requireGitTfs = false)
    {
        Logger.LogInfo("Checking required tools...");
        
        // Check git
        var gitCheck = await VerifyGitAsync();
        if (!gitCheck)
        {
            Logger.LogError("Git is not installed or not properly configured!");
            Logger.LogInfo("Please install Git using one of the following methods:");
            Logger.LogInfo("1. Windows: Use the 'install-git' command or download from https://git-scm.com/download/win");
            Logger.LogInfo("2. macOS: Run 'brew install git' or download from https://git-scm.com/download/mac");
            Logger.LogInfo("3. Linux: Use your distribution's package manager (apt, yum, etc.)");
            return false;
        }
        
        // Check GitHub CLI
        var ghCheck = await VerifyGitHubCliAsync();
        if (!ghCheck)
        {
            Logger.LogError("GitHub CLI is not installed or not properly configured!");
            Logger.LogInfo("Please install GitHub CLI using one of the following methods:");
            Logger.LogInfo("1. Windows: winget install -e --id GitHub.cli or download from https://cli.github.com/");
            Logger.LogInfo("2. macOS: Run 'brew install gh'");
            Logger.LogInfo("3. Linux: Use your distribution's package manager or download from https://cli.github.com/");
            return false;
        }
        
        // Only check git-tfs if required
        if (requireGitTfs)
        {
            var gitTfsCheck = await VerifyGitTfsAsync();
            if (!gitTfsCheck)
            {
                Logger.LogError("git-tfs is not installed or not properly configured!");
                Logger.LogInfo("Please install git-tfs using one of the following methods:");
                Logger.LogInfo("1. Windows: Use the 'install-git-tfs' command");
                Logger.LogInfo("2. Manual: Download from https://github.com/git-tfs/git-tfs/releases");
                return false;
            }
        }
        
        Logger.LogSuccess("All required tools are available.");
        return true;
    }

    /// <summary>
    /// Verifies that Git is properly installed and accessible
    /// </summary>
    /// <returns>True if Git is available and working, false otherwise</returns>
    public static async Task<bool> VerifyGitAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.LogError("Failed to start Git version check process");
                return false;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            var success = process.ExitCode == 0;

            if (success)
            {
                Logger.LogSuccess($"Git {output.Trim()} is installed and working correctly");
            }
            else
            {
                Logger.LogError("Git verification failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error verifying Git installation: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Verifies that git-tfs is properly installed and accessible
    /// </summary>
    /// <returns>True if git-tfs is available and working, false otherwise</returns>
    public static async Task<bool> VerifyGitTfsAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "tfs --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.LogError("Failed to start git-tfs version check process");
                return false;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            var success = process.ExitCode == 0;

            if (success)
            {
                Logger.LogSuccess($"git-tfs {output.Trim()} is installed and working correctly");
            }
            else
            {
                Logger.LogError("git-tfs verification failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error verifying git-tfs installation: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Verifies that GitHub CLI is properly installed and accessible
    /// </summary>
    /// <returns>True if GitHub CLI is available and working, false otherwise</returns>
    public static async Task<bool> VerifyGitHubCliAsync()
    {
        try
        {
            Logger.LogInfo("Checking GitHub CLI installation...");
            var (success, output, error) = await ProcessRunner.RunProcessAsync("gh", "--version", timeoutSeconds: 10);
            
            if (!success)
            {
                Logger.LogWarning("GitHub CLI check failed");
                Logger.LogDebug($"Error: {error}");
                return false;
            }
            
            Logger.LogSuccess($"GitHub CLI is available: {output.Split('\n')[0].Trim()}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"GitHub CLI check failed: {ex.Message}");
            return false;
        }
    }
}