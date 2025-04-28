using AzureDevOps2GitHubMigrator.Models;
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

        /// <summary>
        /// Converts an object to its JSON string representation
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>JSON string representation of the object</returns>
        public static string ToJson<T>(this T obj)
        {
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static string GetAdoApiVersion(string adoVersion) => adoVersion switch
        {
            "2015" => "2.0",
            "2017" => "3.0",
            "2018" => "4.1",
            "2019" => "5.1",
            "2020" => "6.0",
            "2022" => "6.0",
            _ => "7.1"
        };


        internal static void ClearReadOnlyAttributes(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(file);
                    if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to clear read-only attribute for {file}: {ex.Message}");
                }
            }
        }

        internal static async Task SafeDeleteDirectoryAsync(string path, bool isRetry = false)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                // Clear read-only attributes first
                ClearReadOnlyAttributes(path);

                // Force garbage collection to release file handles
                if (isRetry)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Task.Delay(500); // Short delay before retry
                }

                Directory.Delete(path, true);
            }
            catch (UnauthorizedAccessException) when (!isRetry)
            {
                // Retry once with garbage collection
                await SafeDeleteDirectoryAsync(path, true);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to delete directory {path}: {ex.Message}");
            }
        }

        private const int MinimumRequiredColumns = 4;
        private static class CsvColumns
        {
            public const int AdoUser = 0;
            public const int Name = 1;
            public const int Email = 2;
            public const int GitHubUser = 3;
        }

        public static List<MappingUser> LoadUsersMapping(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "Users mapping file path cannot be null or empty");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The specified users mapping file does not exist: {filePath}");
            }

            try
            {
                var usersMapping = new List<MappingUser>();
                using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(filePath)
                {
                    TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
                    Delimiters = new[] { "," },
                    HasFieldsEnclosedInQuotes = true,
                    TrimWhiteSpace = true
                };

                // Skip header row
                if (!parser.EndOfData) parser.ReadLine();

                while (!parser.EndOfData)
                {
                    try
                    {
                        var fields = parser.ReadFields();
                        if (fields == null || fields.Length < MinimumRequiredColumns)
                        {
                            Logger.LogWarning($"Skipping invalid line in mapping file: insufficient columns");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(fields[CsvColumns.AdoUser]) || 
                            string.IsNullOrWhiteSpace(fields[CsvColumns.GitHubUser]))
                        {
                            Logger.LogWarning($"Skipping line with missing required fields (ADO User or GitHub User)");
                            continue;
                        }

                        usersMapping.Add(new MappingUser
                        {
                            AdoUser = fields[CsvColumns.AdoUser].Trim(),
                            Name = fields[CsvColumns.Name].Trim(),
                            Email = fields[CsvColumns.Email].Trim(),
                            GitHubUser = fields[CsvColumns.GitHubUser].Trim()
                        });
                    }
                    catch (Microsoft.VisualBasic.FileIO.MalformedLineException ex)
                    {
                        Logger.LogWarning($"Skipping malformed line in mapping file: {ex.Message}");
                    }
                }

                if (!usersMapping.Any())
                {
                    throw new InvalidOperationException("No valid user mappings found in the file");
                }

                return usersMapping;
            }
            catch (Exception ex) when (ex is not FileNotFoundException && ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"Failed to parse the users mapping file: {filePath}", ex);
            }
        }

        /// <summary>
        /// Creates a GitHub repository name based on the provided pattern and project/repo names
        /// </summary>
        public static string CreateRepositoryName(string? orgName, string? projectName, string? repoName, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(repoName))
                return string.Empty;

            string githubRepoName;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                githubRepoName = (repoName == projectName) ? repoName : $"{projectName}-{repoName}";
            }
            else
            {
                githubRepoName = pattern
                    .Replace("{orgName}", orgName)
                    .Replace("{projectName}", projectName)
                    .Replace("{repoName}", repoName);
                
                // Normalize the name to avoid duplicates due to Azure DevOps default behavior
                if (githubRepoName.Contains($"{projectName}-{projectName}"))
                {
                    githubRepoName = githubRepoName.Replace($"{projectName}-{projectName}", projectName);
                }
            }
            
            return NormalizeName(githubRepoName);
        }

        /// <summary>
        /// Creates a GitHub team name based on the provided pattern and project/team names
        /// </summary>
        public static string CreateTeamName(string? orgName, string? projectName, string? teamName, string? pattern = null)
        {
            if (string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(teamName))
                return string.Empty;

            string githubTeamName;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                githubTeamName = teamName;
            }
            else
            {
                githubTeamName = pattern
                    .Replace("{orgName}", orgName)
                    .Replace("{projectName}", projectName)
                    .Replace("{teamName}", teamName);
                
                // Normalize the name to avoid duplicates
                if (githubTeamName.Contains($"{projectName}-{projectName}"))
                {
                    githubTeamName = githubTeamName.Replace($"{projectName}-{projectName}", projectName);
                }
            }
            
            return NormalizeName(githubTeamName);
        }
    }
}
