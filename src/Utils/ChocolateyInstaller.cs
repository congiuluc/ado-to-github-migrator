using System.Diagnostics;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Provides functionality for managing Chocolatey package manager installation and operations
/// </summary>
public static class ChocolateyInstaller
{
    /// <summary>
    /// Checks if Chocolatey package manager is installed on the system
    /// </summary>
    public static async Task<bool> IsInstalledAsync()
    {
        var result = await ProcessRunner.RunProcessAsync("choco", "--version");
        return result.success;
    }

    /// <summary>
    /// Installs Chocolatey package manager if not already present
    /// </summary>
    /// <returns>True if installation was successful, false otherwise</returns>
    public static async Task<bool> InstallAsync()
    {
        try
        {
            Logger.LogInfo("Installing Chocolatey...");
            
            var result = await ProcessRunner.RunProcessAsync(
                "powershell",
                "-NoProfile -ExecutionPolicy Bypass -Command \"[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))\"");

            if (result.success)
            {
                Logger.LogSuccess("Chocolatey installed successfully");
                return true;
            }

            Logger.LogError("Chocolatey installation failed");
            if (!string.IsNullOrEmpty(result.error))
            {
                Logger.LogError($"Error output: {result.error}");
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to install Chocolatey: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Ensures Chocolatey is installed, installing it if necessary
    /// </summary>
    /// <returns>True if Chocolatey is available (either pre-existing or newly installed), false otherwise</returns>
    public static async Task<bool> EnsureInstalledAsync()
    {
        if (await IsInstalledAsync())
        {
            return true;
        }

        Logger.LogWarning("Chocolatey is not installed. Installing Chocolatey first...");
        return await InstallAsync();
    }
}