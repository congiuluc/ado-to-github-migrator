using System;
using System.IO;

namespace AzureDevOps2GitHubMigrator.Utils
{
    /// <summary>
    /// Provides standardized logging functionality with colored output
    /// </summary>
    public static class Logger
    {
        private static string logFilePath;
        private static readonly object lockObj = new object();

        static Logger()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"migration_{timestamp}.log");
        }

        public static void LogInfo(string message)
        {
            WriteToConsole(message, ConsoleColor.White);
            WriteToFile("INFO", message);
        }

        public static void LogSuccess(string message)
        {
            WriteToConsole(message, ConsoleColor.Green);
            WriteToFile("SUCCESS", message);
        }

        public static void LogError(string message, Exception ex = null)
        {
            WriteToConsole(message, ConsoleColor.Red);
            if (ex != null)
            {
                WriteToConsole(ex.ToString(), ConsoleColor.Red);
            }
            WriteToFile("ERROR", message);
            if (ex != null)
            {
                WriteToFile("ERROR", ex.ToString());
            }
        }

        public static void LogWarning(string message)
        {
            WriteToConsole(message, ConsoleColor.Yellow);
            WriteToFile("WARNING", message);
        }

        private static void WriteToConsole(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        private static void WriteToFile(string level, string message)
        {
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            lock (lockObj)
            {
                File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
            }
        }

        /// <summary>
        /// Prompts for user input with the specified message and returns the input
        /// </summary>
        /// <param name="prompt">The prompt message to display</param>
        /// <returns>The user's input, trimmed of whitespace</returns>
        public static string PromptForInput(string prompt)
        {
            LogInfo(prompt);
            return Console.ReadLine()?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Prompts for user confirmation with the specified message
        /// </summary>
        /// <param name="prompt">The confirmation message to display</param>
        /// <param name="confirmText">The text that indicates confirmation (default: "yes")</param>
        /// <returns>True if user confirmed, false otherwise</returns>
        public static bool PromptForConfirmation(string prompt, string confirmText = "yes")
        {
            LogWarning(prompt);
            var response = Console.ReadLine()?.Trim().ToLower() ?? string.Empty;
            return response == confirmText.ToLower();
        }

        /// <summary>
        /// Waits for user to press Enter to continue
        /// </summary>
        /// <param name="message">Optional message to display (default: "Press Enter to continue...")</param>
        public static void WaitForEnter(string message = "\nPress Enter to continue...")
        {
            LogInfo(message);
            Console.ReadLine();
        }

        /// <summary>
        /// Clears the console screen
        /// </summary>
        public static void ClearScreen()
        {
            Console.Clear();
        }
    }
}