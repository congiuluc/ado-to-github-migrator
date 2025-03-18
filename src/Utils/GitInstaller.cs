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

    /// <summary>
    /// Attempts to install Git using available installation methods
    /// </summary>
    /// <returns>True if installation was successful through any method</returns>
    public static async Task<bool> InstallGitAsync()
    {
        Logger.LogInfo("Starting Git installation process...");
        Logger.LogInfo("Will try multiple installation methods in order: Winget -> Chocolatey -> Direct Download");

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
                "winget",
                "install --id Git.Git --accept-source-agreements --accept-package-agreements");

            if (result.success)
            {
                Logger.LogSuccess("Git installation with winget completed successfully.");
                Logger.LogInfo("Verifying installation...");
                var verified = await VerifyGitInstallationAsync();
                if (verified)
                {
                    Logger.LogSuccess("✓ Git is now properly installed and accessible from command line");
                    return true;
                }
                Logger.LogWarning("! Installation appeared successful but Git is not accessible. May need to restart terminal.");
            }
            Logger.LogWarning("Winget installation attempt failed, will try alternative methods...");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to install Git using winget:", ex);
            if (ex.Message.Contains("not recognized"))
            {
                Logger.LogWarning("It appears winget is not installed on this system.");
            }
        }
        return false;
    }

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
            var result = await ProcessRunner.RunProcessAsync("choco", "install git -y");
            
            if (result.success)
            {
                Logger.LogSuccess("Git installation via Chocolatey completed successfully.");
                Logger.LogInfo("Verifying installation...");
                var verified = await VerifyGitInstallationAsync();
                if (verified)
                {
                    Logger.LogSuccess("✓ Git is now properly installed and accessible from command line");
                    return true;
                }
                Logger.LogWarning("! Installation appeared successful but Git is not accessible. May need to restart terminal.");
            }
            Logger.LogWarning("Chocolatey installation attempt failed, will try direct download...");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to install Git using Chocolatey:", ex);
            if (ex.Message.Contains("not recognized"))
            {
                Logger.LogWarning("Chocolatey installation may have failed or system PATH was not updated.");
            }
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
            var installerPath = Path.Combine(tempDir, "git-installer.exe");

            Logger.LogInfo("Fetching latest Git release information from GitHub...");
            var releaseUrl = "https://api.github.com/repos/git-for-windows/git/releases/latest";
            var response = await _httpClient.GetStringAsync(releaseUrl);
            var releaseInfo = JsonSerializer.Deserialize<JsonElement>(response);
            
            // Find 64-bit installer asset
            var assets = releaseInfo.GetProperty("assets").EnumerateArray();
            string? downloadUrl = null;
            foreach (var asset in assets)
            {
                var name = asset.GetProperty("name").GetString();
                if (name != null && name.Contains("64-bit.exe"))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                Logger.LogError("Could not find Git download URL from GitHub releases.");
                throw new Exception("Could not find Git download URL");
            }

            Logger.LogInfo($"Found latest Git release. Starting download...");
            Logger.LogInfo($"Downloading from: {downloadUrl}");
            var installerBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(installerPath, installerBytes);
            Logger.LogSuccess("Download completed successfully.");

            Logger.LogInfo("Starting Git installer...");
            Logger.LogInfo("This may take a few minutes. Please wait...");
            var result = await ProcessRunner.RunProcessAsync(
                installerPath,
                "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                timeoutSeconds: 600); // Increased timeout for installation

            if (result.success)
            {
                var verified = await VerifyGitInstallationAsync();
                if (verified)
                {
                    Logger.LogSuccess("✓ Git installation completed successfully and is accessible from command line");
                    return true;
                }
                else
                {
                    Logger.LogWarning("! Installation completed but Git is not accessible. You may need to:");
                    Logger.LogWarning("  1. Restart your terminal");
                    Logger.LogWarning("  2. Check if Git is in your system PATH");
                    Logger.LogWarning("  3. Try installing Git manually from https://git-scm.com/downloads");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to install Git via direct download:", ex);
            if (ex is HttpRequestException)
            {
                Logger.LogError("Failed to download Git installer. Please check your internet connection.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                    Logger.LogInfo("Cleaned up temporary installation files.");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Warning: Failed to clean up temporary files at {tempDir}");
                    Logger.LogWarning($"Error details: {ex.Message}");
                }
            }
        }
        return false;
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