using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AzureDevOps2GitHubMigrator.Utils;
using Polly;
using Polly.Retry;

public class GitHubApiRetryPolicy
{
    public static AsyncRetryPolicy<HttpResponseMessage> Create( int maxRetries = 3)
    {
        return Policy
            .HandleResult<HttpResponseMessage>(response =>
                // GitHub specific status codes that warrant a retry
                response.StatusCode == HttpStatusCode.TooManyRequests || // 429 Too Many Requests
                response.StatusCode == HttpStatusCode.Forbidden || // 403 Forbidden (when related to rate limiting)
                response.StatusCode == HttpStatusCode.ServiceUnavailable || // 503 Service Unavailable
                (int)response.StatusCode >= 500) // Any 5xx server error
            .WaitAndRetryAsync(
                maxRetries,
                (retryAttempt, response, context) =>
                {
                    var httpResponse = response.Result;
                    TimeSpan retryAfter;
                    
                    // GitHub rate limiting headers
                    if (httpResponse.StatusCode == HttpStatusCode.Forbidden || 
                        httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        // Check for rate limit reset header
                        if (httpResponse.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
                        {
                            if (long.TryParse(resetValues.FirstOrDefault(), out var resetTimestamp))
                            {
                                // X-RateLimit-Reset is in Unix epoch seconds
                                var resetDate = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
                                var delay = resetDate - DateTimeOffset.UtcNow;
                                
                                // Add a small buffer to ensure the rate limit has reset
                                retryAfter = delay.TotalMilliseconds > 0 
                                    ? delay.Add(TimeSpan.FromSeconds(1)) 
                                    : TimeSpan.FromSeconds(2);

                                Logger.LogWarning($"GitHub API rate limit exceeded. Reset at {resetDate}. Retrying in {retryAfter.TotalSeconds} seconds (Attempt {retryAttempt}/{maxRetries})");

                                return retryAfter;
                            }
                        }
                        
                        // Check for Retry-After header as fallback
                        if (httpResponse.Headers.RetryAfter != null)
                        {
                            if (httpResponse.Headers.RetryAfter.Delta.HasValue)
                            {
                                retryAfter = httpResponse.Headers.RetryAfter.Delta.Value;
                                Logger.LogWarning($"GitHub API request throttled. Retrying in {retryAfter.TotalSeconds} seconds (Attempt {retryAttempt}/{maxRetries})");
                                return retryAfter;
                            }
                            else if (httpResponse.Headers.RetryAfter.Date.HasValue)
                            {
                                retryAfter = httpResponse.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                                Logger.LogWarning($"GitHub API request throttled. Retrying at {httpResponse.Headers.RetryAfter.Date.Value} (Attempt {retryAttempt}/{maxRetries})");
                                return retryAfter;
                            }
                        }
                    }
                    
                    // Secondary rate limit (abuse detection)
                    if (httpResponse.Headers.TryGetValues("Retry-After", out var retryValues))
                    {
                        if (int.TryParse(retryValues.FirstOrDefault(), out var seconds))
                        {
                            retryAfter = TimeSpan.FromSeconds(seconds);
                            Logger.LogWarning($"GitHub API secondary rate limit triggered. Retrying in {retryAfter.TotalSeconds} seconds (Attempt {retryAttempt}/{maxRetries})");
                            return retryAfter;
                        }
                    }
                    
                    // Default exponential backoff with jitter for other cases
                    Random jitter = Random.Shared;
                    retryAfter = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + 
                                TimeSpan.FromMilliseconds(jitter.Next(0, 1000));

                    Logger.LogWarning($"GitHub API request failed with status code {httpResponse.StatusCode}. Retrying in {retryAfter.TotalSeconds} seconds (Attempt {retryAttempt}/{maxRetries})");

                    return retryAfter;
                },                (outcome, timeSpan, retryCount, context) =>
                {
                    // Log detailed information before each retry
                    var statusCode = outcome.Result.StatusCode;
                    
                    // Log rate limit information if available
                    if (outcome.Result.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                        outcome.Result.Headers.TryGetValues("X-RateLimit-Limit", out var limit))
                    {
                        Logger.LogWarning($"GitHub API rate limit: {remaining.FirstOrDefault()}/{limit.FirstOrDefault()} remaining");
                    }
                    
                    // Try to log response body for more details on error
                    Task.Run(async () => {
                        try
                        {
                            var content = await outcome.Result.Content.ReadAsStringAsync();
                            if (!string.IsNullOrEmpty(content))
                            {
                                Logger.LogWarning($"GitHub API error response: {content}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Could not read GitHub API error response: {ex.Message}");
                        }
                    });
                    
                    return Task.CompletedTask;
                }
            );
    }
}