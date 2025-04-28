using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Handles Git installation on Windows systems using multiple installation methods.
/// Supports installation via Chocolatey package manager and direct download from git-scm.com.
/// </summary>
/// <remarks>
/// This class provides fallback mechanisms to ensure Git is available:
/// 1. Attempts to use existing Git installation
/// 2. Tries to install via Chocolatey if available
/// 3. Downloads and installs Git directly if needed
/// </remarks>
public class GitInstaller
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
            public const string InstallArgs = "install --id Git.Git --accept-source-agreements --accept-package-agreements";
        }
        
        public static class Chocolatey
        {
            public const string Command = "choco";
            public const string InstallArgs = "install git -y";
        }
        
        public static class DirectDownload
        {
            public const string ReleaseApiUrl = "https://api.github.com/repos/git-for-windows/git/releases/latest";
            public const string InstallerArgs = "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
            public const string Installer64BitPattern = "64-bit.exe";
        }
    }

    /// <summary>
    /// Attempts to install Git using available installation methods
    /// </summary>
    /// <returns>True if installation was successful through any method</returns>
    public static async Task<bool> InstallGitAsync()
    {
        Logger.LogInfo("Starting Git installation process...");
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

        Logger.LogError("\nAll installation methods failed. Please try installing Git manually from https://git-scm.com/downloads");
        return false;
    }

    /// <summary>
    /// Attempts to install Git using Windows Package Manager (winget)
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
                Logger.LogSuccess("Git installation with winget completed successfully.");
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
    /// Attempts to install Git using Chocolatey package manager
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

            Logger.LogInfo("Installing Git via Chocolatey...");
            var result = await ProcessRunner.RunProcessAsync(
                InstallConfig.Chocolatey.Command, 
                InstallConfig.Chocolatey.InstallArgs,
                timeoutSeconds: InstallConfig.DefaultTimeoutSeconds);
            
            if (result.success)
            {
                Logger.LogSuccess("Git installation via Chocolatey completed successfully.");
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
    /// Attempts to install Git by downloading the installer directly from GitHub releases
    /// This is the fallback method when package managers are not available
    /// </summary>
    private static async Task<bool> TryInstallWithDirectDownloadAsync()
    {
        Logger.LogInfo("Beginning direct download installation process...");
        var tempDir = Path.Combine(Path.GetTempPath(), "git-install");
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
                Logger.LogError("Failed to download Git installer. Please check your internet connection.");
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
            Logger.LogInfo("Fetching latest Git release information from GitHub...");
            var response = await _httpClient.GetStringAsync(InstallConfig.DirectDownload.ReleaseApiUrl);
            var releaseInfo = JsonSerializer.Deserialize<JsonElement>(response);
            
            var downloadUrl = GetInstallerDownloadUrl(releaseInfo);
            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            var installerPath = Path.Combine(tempDir, "git-installer.exe");
            Logger.LogInfo($"Downloading from: {downloadUrl}");
            
            var installerBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(installerPath, installerBytes);
            Logger.LogSuccess("Download completed successfully.");
            
            return installerPath;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to download Git installer: {ex.Message}");
            return null;
        }
    }

    private static string? GetInstallerDownloadUrl(JsonElement releaseInfo)
    {
        var assets = releaseInfo.GetProperty("assets").EnumerateArray();
        foreach (var asset in assets)
        {
            var name = asset.GetProperty("name").GetString();
            if (name != null && name.Contains(InstallConfig.DirectDownload.Installer64BitPattern))
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }
        Logger.LogError("Could not find Git download URL from GitHub releases.");
        return null;
    }

    private static async Task<bool> RunInstallerAsync(string installerPath)
    {
        Logger.LogInfo("Starting Git installer...");
        Logger.LogInfo("This may take a few minutes. Please wait...");
        
        var result = await ProcessRunner.RunProcessAsync(
            installerPath,
            InstallConfig.DirectDownload.InstallerArgs,
            timeoutSeconds: InstallConfig.InstallerTimeoutSeconds);

        if (!result.success)
        {
            Logger.LogError("Git installer failed to complete successfully.");
            return false;
        }

        return await ValidateInstallation();
    }

    private static async Task<bool> ValidateInstallation()
    {
        Logger.LogInfo("Verifying installation...");
        var verified = await VerifyGitInstallationAsync();
        
        if (verified)
        {
            Logger.LogSuccess("✓ Git is now properly installed and accessible from command line");
            return true;
        }
        
        Logger.LogWarning("! Installation completed but Git is not accessible. You may need to:");
        Logger.LogWarning("  1. Restart your terminal");
        Logger.LogWarning("  2. Check if Git is in your system PATH");
        Logger.LogWarning("  3. Try installing Git manually from https://git-scm.com/downloads");
        return false;
    }

    private static void LogInstallationError(string method, Exception ex)
    {
        Logger.LogError($"Failed to install Git using {method}:", ex);
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
    /// Verifies Git installation by checking if the 'git' command is available
    /// and returns version information
    /// </summary>
    /// <returns>
    /// True if Git is properly installed and accessible, false otherwise
    /// </returns>
    /// <remarks>
    /// This method checks both the presence of Git and its ability to execute commands
    /// by running 'git --version'
    /// </remarks>
    private static async Task<bool> VerifyGitInstallationAsync()
    {
        Logger.LogInfo("Verifying Git installation...");
        try
        {
            var result = await ProcessRunner.RunProcessAsync("git", "--version");
            
            if (result.success)
            {
                Logger.LogSuccess($"Git version {result.output.Trim()} is installed and working correctly");
            }
            else
            {
                Logger.LogError("Git verification failed - command returned error");
            }
            return result.success;
        }
        catch (Exception ex)
        {
            Logger.LogError("Git installation verification failed", ex);
            return false;
        }
    }
}