using System.Diagnostics;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Handles the installation and verification of GitHub CLI (gh) on Windows systems.
/// </summary>
/// <remarks>
/// This class provides functionality to:
/// - Check for existing GitHub CLI installations
/// - Install GitHub CLI via multiple methods (winget, Chocolatey, direct download)
/// - Verify GitHub CLI functionality post-installation
/// </remarks>
public class GitHubCliInstaller
{
    private static readonly HttpClient _httpClient = new();
    
    private static class InstallConfig
    {
        public const int DefaultTimeoutSeconds = 300;
        public const int InstallerTimeoutSeconds = 600;
        public const int MaxRetries = 3;
        public const int RetryDelaySeconds = 5;
        
        public static class Winget
        {
            public const string Command = "winget";
            public const string InstallArgs = "install --id GitHub.cli --accept-source-agreements --accept-package-agreements";
        }
        
        public static class Chocolatey
        {
            public const string Command = "choco";
            public const string InstallArgs = "install gh -y";
        }
        
        public static class DirectDownload
        {
            public const string ReleaseApiUrl = "https://api.github.com/repos/cli/cli/releases/latest";
            public const string InstallerArgs = "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
            public const string InstallerPattern = "windows-amd64.msi";
        }
    }

    /// <summary>
    /// Ensures GitHub CLI is available on the system, installing it if necessary
    /// </summary>
    /// <returns>True if GitHub CLI is available and working, false otherwise</returns>
    public static async Task<bool> EnsureGitHubCliInstalledAsync()
    {
        Logger.LogInfo("Checking GitHub CLI installation...");
        
        if (await RequiredModulesChecker.VerifyGitHubCliAsync())
        {
            Logger.LogSuccess("GitHub CLI is already installed and working.");
            return true;
        }

        Logger.LogWarning("GitHub CLI is not available. Attempting installation...");
        return await InstallGitHubCliAsync();
    }

    /// <summary>
    /// Attempts to install GitHub CLI using available installation methods
    /// </summary>
    /// <returns>True if installation was successful through any method</returns>
    public static async Task<bool> InstallGitHubCliAsync()
    {
        Logger.LogInfo("Starting GitHub CLI installation process...");
        Logger.LogInfo("Will try multiple installation methods in order: Winget -> Chocolatey -> Direct Download");

        for (int attempt = 1; attempt <= InstallConfig.MaxRetries; attempt++)
        {
            if (attempt > 1)
            {
                Logger.LogInfo($"\nRetry attempt {attempt} of {InstallConfig.MaxRetries}...");
                await Task.Delay(TimeSpan.FromSeconds(InstallConfig.RetryDelaySeconds));
            }

            // Try winget first
            Logger.LogInfo("\n=== Attempting installation via Windows Package Manager (Winget) ===");
            if (await TryInstallWithWingetAsync())
                return true;

            // Try Chocolatey
            Logger.LogInfo("\n=== Attempting installation via Chocolatey Package Manager ===");
            if (await TryInstallWithChocolateyAsync())
                return true;

            // Try direct download as last resort
            Logger.LogInfo("\n=== Attempting installation via Direct Download ===");
            if (await TryInstallWithDirectDownloadAsync())
                return true;
        }

        Logger.LogError("\nAll installation methods failed. Please try installing GitHub CLI manually from https://cli.github.com/");
        return false;
    }

    /// <summary>
    /// Attempts to install GitHub CLI using Windows Package Manager (winget)
    /// This is the preferred method as it's built into Windows
    /// </summary>
    private static async Task<bool> TryInstallWithWingetAsync()
    {
        Logger.LogInfo("Checking if winget is available on the system...");
        try
        {
            var result = await ProcessRunner.RunProcessAsync(
                InstallConfig.Winget.Command,
                InstallConfig.Winget.InstallArgs,
                timeoutSeconds: InstallConfig.DefaultTimeoutSeconds);

            if (result.success)
            {
                Logger.LogSuccess("GitHub CLI installation with winget completed successfully.");
                return await ValidateInstallation();
            }
            
            Logger.LogWarning("Winget installation attempt failed, will try alternative methods...");
        }
        catch (Exception ex)
        {
            LogInstallationError("winget", ex);
        }
        return false;
    }

    /// <summary>
    /// Attempts to install GitHub CLI using Chocolatey package manager
    /// </summary>
    private static async Task<bool> TryInstallWithChocolateyAsync()
    {
        try
        {
            if (!await ChocolateyInstaller.EnsureInstalledAsync())
            {
                Logger.LogError("Failed to ensure Chocolatey is installed.");
                return false;
            }

            Logger.LogInfo("Installing GitHub CLI via Chocolatey...");
            var result = await ProcessRunner.RunProcessAsync(
                InstallConfig.Chocolatey.Command, 
                InstallConfig.Chocolatey.InstallArgs,
                timeoutSeconds: InstallConfig.DefaultTimeoutSeconds);
            
            if (result.success)
            {
                Logger.LogSuccess("GitHub CLI installation via Chocolatey completed successfully.");
                return await ValidateInstallation();
            }
            
            Logger.LogWarning("Chocolatey installation attempt failed, will try direct download...");
        }
        catch (Exception ex)
        {
            LogInstallationError("Chocolatey", ex);
        }
        return false;
    }

    /// <summary>
    /// Attempts to install GitHub CLI by downloading the installer directly from GitHub releases
    /// This is the fallback method when package managers are not available
    /// </summary>
    private static async Task<bool> TryInstallWithDirectDownloadAsync()
    {
        Logger.LogInfo("Beginning direct download installation process...");
        var tempDir = Path.Combine(Path.GetTempPath(), "gh-install");
        try
        {
            Directory.CreateDirectory(tempDir);
            var installerPath = await DownloadLatestInstallerAsync(tempDir);
            
            if (string.IsNullOrEmpty(installerPath))
                return false;

            return await RunInstallerAsync(installerPath);
        }
        catch (Exception ex)
        {
            LogInstallationError("direct download", ex);
            if (ex is HttpRequestException)
            {
                Logger.LogError("Failed to download GitHub CLI installer. Please check your internet connection.");
            }
        }
        finally
        {
            await CleanupTempFilesAsync(tempDir);
        }
        return false;
    }

    private static async Task<string?> DownloadLatestInstallerAsync(string tempDir)
    {
        try
        {
            Logger.LogInfo("Fetching latest GitHub CLI release information from GitHub...");
            var response = await _httpClient.GetStringAsync(InstallConfig.DirectDownload.ReleaseApiUrl);
            var releaseInfo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(response);
            
            var downloadUrl = GetInstallerDownloadUrl(releaseInfo);
            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            var installerPath = Path.Combine(tempDir, "gh-installer.msi");
            Logger.LogInfo($"Downloading from: {downloadUrl}");
            
            var installerBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(installerPath, installerBytes);
            Logger.LogSuccess("Download completed successfully.");
            
            return installerPath;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to download GitHub CLI installer: {ex.Message}");
            return null;
        }
    }

    private static string? GetInstallerDownloadUrl(System.Text.Json.JsonElement releaseInfo)
    {
        var assets = releaseInfo.GetProperty("assets").EnumerateArray();
        foreach (var asset in assets)
        {
            var name = asset.GetProperty("name").GetString();
            if (name != null && name.Contains(InstallConfig.DirectDownload.InstallerPattern))
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }
        Logger.LogError("Could not find GitHub CLI download URL from GitHub releases.");
        return null;
    }

    private static async Task<bool> RunInstallerAsync(string installerPath)
    {
        Logger.LogInfo("Starting GitHub CLI installer...");
        Logger.LogInfo("This may take a few minutes. Please wait...");
        
        var result = await ProcessRunner.RunProcessAsync(
            "msiexec",
            $"/i \"{installerPath}\" /quiet /norestart",
            timeoutSeconds: InstallConfig.InstallerTimeoutSeconds);

        if (!result.success)
        {
            Logger.LogError("GitHub CLI installer failed to complete successfully.");
            return false;
        }

        return await ValidateInstallation();
    }

    private static async Task<bool> ValidateInstallation()
    {
        Logger.LogInfo("Verifying installation...");
        var verified = await VerifyGitHubCliInstallationAsync();
        
        if (verified)
        {
            Logger.LogSuccess("✓ GitHub CLI is now properly installed and accessible from command line");
            return true;
        }
        
        Logger.LogWarning("! Installation completed but GitHub CLI is not accessible. You may need to:");
        Logger.LogWarning("  1. Restart your terminal");
        Logger.LogWarning("  2. Check if GitHub CLI is in your system PATH");
        Logger.LogWarning("  3. Try installing GitHub CLI manually from https://cli.github.com/");
        return false;
    }

    private static void LogInstallationError(string method, Exception ex)
    {
        Logger.LogError($"Failed to install GitHub CLI using {method}:", ex);
        if (ex.Message.Contains("not recognized"))
        {
            Logger.LogWarning($"{method} is not available on this system.");
        }
    }

    private static async Task CleanupTempFilesAsync(string tempDir)
    {
        if (Directory.Exists(tempDir))
        {
            try
            {
                await Task.Run(() => Directory.Delete(tempDir, true));
                Logger.LogInfo("Cleaned up temporary installation files.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Warning: Failed to clean up temporary files at {tempDir}");
                Logger.LogWarning($"Error details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Verifies GitHub CLI installation by checking if the 'gh' command is available
    /// and returns version information
    /// </summary>
    /// <returns>
    /// True if GitHub CLI is properly installed and accessible, false otherwise
    /// </returns>
    private static async Task<bool> VerifyGitHubCliInstallationAsync()
    {
        Logger.LogInfo("Verifying GitHub CLI installation...");
        try
        {
            var result = await ProcessRunner.RunProcessAsync("gh", "--version");
            
            if (result.success)
            {
                Logger.LogSuccess($"GitHub CLI {result.output.Trim()} is installed and working correctly");
            }
            else
            {
                Logger.LogError("GitHub CLI verification failed - command returned error");
            }
            return result.success;
        }
        catch (Exception ex)
        {
            Logger.LogError("GitHub CLI installation verification failed", ex);
            return false;
        }
    }
}