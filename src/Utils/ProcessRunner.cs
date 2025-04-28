using System.Diagnostics;
using System.Text;

namespace AzureDevOps2GitHubMigrator.Utils;

/// <summary>
/// Provides utility methods for executing shell commands and processes with consistent error handling and output capture.
/// This class is used throughout the application to standardize process execution and logging.
/// </summary>
/// <remarks>
/// Features:
/// - Asynchronous process execution with timeout support
/// - Standardized output and error capturing
/// - Configurable working directory
/// - Environment variable management
/// - Process execution state tracking
/// </remarks>
public static class ProcessRunner
{
    /// <summary>
    /// Executes a shell command asynchronously with the specified parameters
    /// </summary>
    /// <param name="command">The command to execute</param>
    /// <param name="arguments">Command line arguments</param>
    /// <param name="workingDirectory">Working directory for the process. If null, uses current directory</param>
    /// <param name="timeoutSeconds">Maximum time in seconds to wait for the process to complete</param>
    /// <returns>
    /// A tuple containing:
    /// - bool: Whether the process completed successfully
    /// - string: Standard output from the process
    /// - string: Error output from the process
    /// </returns>
    /// <remarks>
    /// This method handles:
    /// - Process startup and monitoring
    /// - Output and error stream capturing
    /// - Timeout management
    /// - Process cleanup
    /// </remarks>
    public static async Task<(bool success, string output, string error)> RunProcessAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        int timeoutSeconds = 300)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        try
        {
            Logger.LogDebug($"{command}] {arguments}");
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, string.Empty, $"Failed to start process [{command}]");
            }

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    Logger.LogDebug($"[{command}] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    Logger.LogDebug($"[{command}] {e.Data}");
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Create timeout task
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

            // Wait for process completion or timeout
            var processTask = process.WaitForExitAsync();
            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have completed just as we tried to kill it
                }
                return (false, outputBuilder.ToString(), $"Process [{command}] timed out after {timeoutSeconds} seconds");
            }

            // Process completed within timeout
            cts.Cancel(); // Stop the timeout task

            return (process.ExitCode == 0, outputBuilder.ToString(), errorBuilder.ToString());
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"Process [{command}] execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a shell command synchronously and returns the result
    /// </summary>
    /// <param name="command">The command to execute</param>
    /// <param name="arguments">Command line arguments</param>
    /// <param name="workingDirectory">Working directory for the process. If null, uses current directory</param>
    /// <returns>The exit code of the process</returns>
    /// <remarks>
    /// This is a simplified version of RunProcessAsync for cases where:
    /// - Asynchronous execution is not required
    /// - Output capture is not needed
    /// - Default timeout is acceptable
    /// </remarks>
    public static int RunProcess(string command, string arguments, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}