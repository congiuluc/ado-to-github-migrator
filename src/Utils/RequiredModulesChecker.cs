using System.Diagnostics;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Validates and ensures required external tools and modules are available for the migration process.
/// </summary>
/// <remarks>
/// This class is responsible for:
/// - Checking Git and git-tfs installations.
/// - Dependency validation before migration.
/// - Automated installation of missing components.
/// - Version compatibility checks.
/// </remarks>
public static class RequiredModulesChecker
{
    /// <summary>
    /// Checks and installs required modules for repository migration
    /// </summary>
    /// <param name="requireGitTfs">Indicates whether git-tfs is required (true for TFVC migrations).</param>
    /// <returns>Returns true if all required modules are available, otherwise false.</returns>
    /// <exception cref="Exception">Throws an exception if any error occurs during the validation process.</exception>
    /// <remarks>
    /// Performs the following checks:
    /// 1. Verifies Git installation
    /// 2. Installs Git if missing
    /// 3. Optionally checks and installs git-tfs
    /// </remarks>
    public static async Task<bool> EnsureRequiredModulesAsync(bool requireGitTfs = false)
    {
        Logger.LogInfo("Checking required modules...");

        // Check Git installation
        var gitInstalled = await VerifyGitAsync();
        if (!gitInstalled)
        {
            Logger.LogWarning("Git is not installed. Attempting to install...");
            gitInstalled = await GitInstaller.InstallGitAsync();
            if (!gitInstalled)
            {
                Logger.LogError("Failed to install Git. Please install it manually from https://git-scm.com/downloads");
                return false;
            }
        }

        // Check git-tfs if required
        if (requireGitTfs)
        {
            Logger.LogInfo("Checking git-tfs installation...");
            var gitTfsInstalled = await VerifyGitTfsAsync();
            if (!gitTfsInstalled)
            {
                gitTfsInstalled = await GitTfsInstaller.EnsureGitTfsInstalledAsync();
                if (!gitTfsInstalled)
                {
                    Logger.LogError("Failed to install git-tfs. Please install it manually using: choco install gittfs");
                    return false;
                }
            }
        }

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
}