using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Utils
{
    /// <summary>
    /// Provides common utility functions and constants used throughout the application.
    /// This class centralizes shared functionality to maintain consistency and reduce code duplication.
    /// </summary>
    public static class Common
    {
        /// <summary>
        /// Maximum length allowed for GitHub repository names
        /// </summary>
        /// <remarks>
        /// As per GitHub documentation:
        /// https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-repository
        /// </remarks>
        public const int MaxGitHubRepoNameLength = 100;

        /// <summary>
        /// Regular expression pattern for validating GitHub repository names
        /// </summary>
        /// <remarks>
        /// Ensures names:
        /// - Start with an alphanumeric character
        /// - Can contain hyphens and underscores
        /// - Don't have consecutive dots
        /// - Don't end with a dot
        /// </remarks>
        private static readonly Regex RepoNamePattern = new(@"^[a-zA-Z0-9][-\w.]*[a-zA-Z0-9]$");

        /// <summary>
        /// Normalizes a name for use in GitHub (repository names, team names, etc.)
        /// by removing special characters, converting spaces to hyphens, and ensuring
        /// it follows GitHub naming conventions.
        /// </summary>
        /// <param name="name">The original name to normalize</param>
        /// <returns>A normalized name suitable for GitHub</returns>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            // Convert to lowercase
            string normalized = name.ToLowerInvariant();

            // Replace spaces and underscores with hyphens
            normalized = normalized.Replace(' ', '-').Replace('_', '-');

            // Remove any character that isn't alphanumeric or hyphen
            normalized = Regex.Replace(normalized, "[^a-z0-9-]", "");

            // Replace multiple consecutive hyphens with a single hyphen
            normalized = Regex.Replace(normalized, "-+", "-");

            // Remove leading and trailing hyphens
            normalized = normalized.Trim('-');

            return normalized;
        }

        /// <summary>
        /// Sanitizes a string for use as a GitHub repository name
        /// </summary>
        /// <param name="input">The original repository name</param>
        /// <returns>A valid GitHub repository name</returns>
        /// <remarks>
        /// Applies the following transformations:
        /// - Removes invalid characters
        /// - Replaces spaces with hyphens
        /// - Ensures length constraints
        /// - Ensures valid start and end characters
        /// </remarks>
        public static string SanitizeRepositoryName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "repository";

            // Convert to lowercase and replace spaces with hyphens
            var sanitized = input.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace(".", "-")
                .Replace("_", "-");

            // Remove any character that's not alphanumeric or hyphen
            sanitized = Regex.Replace(sanitized, "[^a-z0-9\\-]", "");

            // Remove consecutive hyphens
            sanitized = Regex.Replace(sanitized, "-+", "-");

            // Ensure it starts with a letter or number
            if (!char.IsLetterOrDigit(sanitized[0]))
                sanitized = "r" + sanitized;

            // Ensure it ends with a letter or number
            if (sanitized.Length > 1 && !char.IsLetterOrDigit(sanitized[^1]))
                sanitized = sanitized[..^1];

            // Ensure maximum length
            if (sanitized.Length > MaxGitHubRepoNameLength)
                sanitized = sanitized[..MaxGitHubRepoNameLength];

            return sanitized;
        }

        /// <summary>
        /// Validates if a string is a valid GitHub repository name
        /// </summary>
        /// <param name="name">The repository name to validate</param>
        /// <returns>True if the name is valid, false otherwise</returns>
        /// <remarks>
        /// Checks for:
        /// - Non-empty string
        /// - Valid length
        /// - Allowed characters
        /// - Valid pattern matching
        /// </remarks>
        public static bool IsValidRepositoryName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (name.Length > MaxGitHubRepoNameLength)
                return false;

            return RepoNamePattern.IsMatch(name);
        }

        /// <summary>
        /// Formats a byte size into a human-readable string with appropriate units
        /// </summary>
        /// <param name="bytes">Size in bytes</param>
        /// <returns>Formatted string (e.g., "1.5 MB", "800 KB")</returns>
        public static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Creates a clickable link for console output using ANSI escape sequences
        /// </summary>
        /// <param name="text">Display text for the link</param>
        /// <param name="url">Target URL</param>
        /// <returns>Formatted link string that will be clickable in supporting terminals</returns>
        public static string CreateClickableLink(string? text, string? url)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(url))
                return text ?? "";

            // Use ANSI escape sequence to create a clickable link
            return $"\u001b]8;;{url}\u0007{text}\u001b]8;;\u0007";
        }

        /// <summary>
        /// Creates a clickable Markdown link
        /// </summary>
        /// <param name="text">Display text for the link</param>
        /// <param name="url">Target URL</param>
        /// <returns>Formatted Markdown link string</returns>
        public static string CreateClickableMarkdownLink(string text, string url)
        {
            if (string.IsNullOrEmpty(url))
                return text;
            return $"[{text}]({url})";
        }

        /// <summary>
        /// Creates a deterministic short hash from a string
        /// </summary>
        /// <param name="input">String to hash</param>
        /// <returns>A 7-character hash string</returns>
        /// <remarks>
        /// Used for generating unique identifiers that are:
        /// - Consistent across runs
        /// - Short enough to be human-readable
        /// - Suitable for file names and IDs
        /// </remarks>
        public static string CreateShortHash(string input)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes).Substring(0, 7).ToLowerInvariant();
        }

        
    }
}
