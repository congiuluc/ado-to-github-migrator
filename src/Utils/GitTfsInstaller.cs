using System.Diagnostics;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Handles the installation and verification of git-tfs, a Git extension that enables bidirectional communication with Team Foundation Version Control (TFVC).
/// </summary>
/// <remarks>
/// This class provides functionality to:
/// - Check for existing git-tfs installations
/// - Install git-tfs via Chocolatey package manager
/// - Verify git-tfs functionality post-installation
/// </remarks>
public class GitTfsInstaller
{
    /// <summary>
    /// Ensures git-tfs is available on the system, installing it if necessary
    /// </summary>
    /// <returns>True if git-tfs is available and working, false otherwise</returns>
    /// <remarks>
    /// The installation process:
    /// 1. Verifies if git-tfs is already installed and working
    /// 2. If not available, attempts installation via Chocolatey
    /// 3. Verifies the installation was successful
    /// </remarks>
    public static async Task<bool> EnsureGitTfsInstalledAsync()
    {
        Logger.LogInfo("Checking git-tfs installation...");
        
        if (await VerifyGitTfsInstallationAsync())
        {
            Logger.LogSuccess("git-tfs is already installed and working.");
            return true;
        }

        Logger.LogWarning("git-tfs is not available. Attempting installation...");
        return await InstallGitTfsAsync();
    }

    /// <summary>
    /// Verifies that git-tfs is properly installed and functioning
    /// </summary>
    /// <returns>True if git-tfs is working correctly, false otherwise</returns>
    /// <remarks>
    /// Checks both the presence of git-tfs and its ability to execute basic commands
    /// </remarks>
    public static async Task<bool> VerifyGitTfsInstallationAsync()
    {
        try
        {
            var result = await ProcessRunner.RunProcessAsync("git", "tfs --version");
            
            if (result.success)
            {
                Logger.LogSuccess($"git-tfs {result.output.Trim()} is installed and working correctly");
                return true;
            }

            Logger.LogError("git-tfs verification failed");
            if (!string.IsNullOrEmpty(result.error))
            {
                Logger.LogError($"Error output: {result.error}");
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to verify git-tfs installation: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Attempts to install git-tfs using Chocolatey package manager or direct download
    /// </summary>
    /// <param name="skipChocolatey">Whether to skip Chocolatey installation and try direct download</param>
    /// <returns>True if installation was successful, false otherwise</returns>
    public static async Task<bool> InstallGitTfsAsync(bool skipChocolatey = false)
    {
        if (!skipChocolatey)
        {
            Logger.LogInfo("Checking Chocolatey installation...");
            if (!await ChocolateyInstaller.EnsureInstalledAsync())
            {
                Logger.LogError("Failed to ensure Chocolatey is installed");
                skipChocolatey = true;
            }
        }

        if (!skipChocolatey)
        {
            Logger.LogInfo("Installing git-tfs via Chocolatey...");
            try
            {
                var result = await ProcessRunner.RunProcessAsync("choco", "install gittfs -y");

                if (result.success)
                {
                    Logger.LogInfo("git-tfs installation completed. Verifying installation...");
                    return await VerifyGitTfsInstallationAsync();
                }
                
                Logger.LogError("git-tfs installation failed");
                if (!string.IsNullOrEmpty(result.error))
                {
                    Logger.LogError($"Error output: {result.error}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to install git-tfs via Chocolatey: {ex.Message}", ex);
            }
        }

        // Try direct download if Chocolatey failed or was skipped
        Logger.LogInfo("Attempting direct download of git-tfs...");
        try
        {
            // TODO: Implement direct download logic
            Logger.LogError("Direct download not implemented yet");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to download git-tfs directly: {ex.Message}", ex);
            return false;
        }
    }
}