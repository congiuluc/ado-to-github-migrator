namespace AzureDevOps2GitHubMigrator.GitHub;

using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;
using Polly;
using Polly.Retry;
using AzureDevOps2GitHubMigrator.Models.GitHub;

/// <summary>
/// Service for interacting with GitHub APIs and resources.
/// </summary>
public class GitHubService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private string _token;
    private readonly string _apiVersion;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly string _defaultTeamMemberRole;

    public GitHubService(
        HttpClient httpClient,
        string token = "",
        string baseUrl = "https://api.github.com",
        string apiVersion = "v3",
        string defaultTeamMemberRole = "member")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
        _apiVersion = apiVersion;
        _defaultTeamMemberRole = defaultTeamMemberRole;

        // Setup auth with token
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue($"application/vnd.github.{apiVersion}+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AzureDevOps2GitHubMigrator", "1.0"));

        // Configure retry policy with exponential backoff
        _retryPolicy = GitHubApiRetryPolicy.Create(maxRetries: 5);
       
    }

    private Uri BuildUrl(string relativeUrl) => new Uri($"{_baseUrl}/{relativeUrl.TrimStart('/')}");

    private async Task<HttpResponseMessage> SendRequestAsync(string relativeUrl, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(relativeUrl));
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.SendAsync(request, cancellationToken));
    }

    private async Task<T?> InvokeApiAsync<T>(string relativeUrl, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogDebug($"Invoking API: {method} {relativeUrl}");

            var response = await SendRequestAsync(relativeUrl, method, body, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.LogDebug($"API returned 404 Not Found: {relativeUrl}");
                return default;
            }            // Log rate limit information from response headers
            LogRateLimitInfo(response);
        
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength == 0)
            {
                Logger.LogDebug($"API response has no content: {relativeUrl}");
                return typeof(T) == typeof(bool) ? (T)(object)true : default;
            }

            var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            Logger.LogDebug($"API call successful: {relativeUrl}");
            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogDebug($"HTTP request exception for API call: {ex.Message}");
            throw new Exception($"GitHub API call failed after retries: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Unexpected exception for API call: {ex.Message}");
            throw;
        }
    }

    private async Task<T?> InvokeGraphQLAsync<T>(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                query
            };

            using var graphQLRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/graphql");
            graphQLRequest.Content = JsonContent.Create(request);            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.SendAsync(graphQLRequest, cancellationToken));
            
            // Log rate limit information from GraphQL API call
            LogRateLimitInfo(response);

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength == 0)
                return default;

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (result.TryGetProperty("data", out var data))
            {
                return JsonSerializer.Deserialize<T>(data.GetRawText());
            }

            if (result.TryGetProperty("errors", out var errors))
            {
                throw new Exception($"GraphQL query failed: {errors}");
            }

            return default;
        }
        catch (Exception ex)
        {
            throw new Exception($"GitHub GraphQL API call failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> TestConnectionAsync(string organization, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Validating GitHub connection for organization: {organization}");
            var response = await InvokeApiAsync<object>($"orgs/{organization}", HttpMethod.Get, cancellationToken: cancellationToken);
            Logger.LogSuccess("GitHub connection validated successfully");
            return response != null;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to connect to GitHub: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> TestRepoCreationPermissionAsync(string organization, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Testing repository creation permissions in organization: {organization}");
            var testRepo = new
            {
                name = $"test-repo-{Guid.NewGuid():N}",
                @private = true,
                auto_init = false,
                description = "Test repository for permission validation"
            };

            var response = await InvokeApiAsync<object>($"orgs/{organization}/repos", HttpMethod.Post, testRepo, cancellationToken);
            if (response == null)
                return false;

            // Clean up test repository
            await InvokeApiAsync<object>($"repos/{organization}/{testRepo.name}", HttpMethod.Delete, cancellationToken: cancellationToken);
            Logger.LogSuccess("Repository creation permission test successful");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to test repository creation: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> ValidateRepositoryNameAsync(string repoName)
    {
        // GitHub repository naming rules
        if (string.IsNullOrWhiteSpace(repoName) ||
            repoName.StartsWith(".") ||
            repoName.EndsWith(".") ||
            repoName.Contains("..") ||
            repoName.Any(c => char.IsWhiteSpace(c) || "~^:?*[\\]|/@<>".Contains(c)))
        {
            throw new ArgumentException($"Invalid repository name: '{repoName}'. GitHub repository names cannot start or end with a dot, contain consecutive dots, spaces, or special characters.");
        }

        return await Task.FromResult(true);
    }

    public async Task<GitHubRepositoryInfo?> CreateRepositoryAsync(string organization, string repoName, string? description = null, bool isPrivate = true, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Creating GitHub repository: {repoName}");
            await ValidateRepositoryNameAsync(repoName);

            var repoData = new
            {
                name = repoName,
                description,
                @private = isPrivate,
                auto_init = false
            };

            var repo = await InvokeApiAsync<GitHubRepository>($"orgs/{organization}/repos", HttpMethod.Post, repoData, cancellationToken);
            if (repo != null)
            {
                Logger.LogSuccess($"Successfully created repository: {repoName}");
            }
            return new GitHubRepositoryInfo
            {
                Id = repo?.id.ToString(),
                Name = repo?.name,
                Description = repo?.description,
                Url = repo?.html_url,
                FullName = repo?.full_name,
                Private = repo?._private ?? true,
                DefaultBranch = repo?.default_branch,
                HtmlUrl = repo?.html_url,
                CloneUrl = repo?.clone_url,
                CreatedAt = repo?.created_at,
                UpdatedAt = repo?.updated_at,
                PushedAt = repo?.pushed_at,
                Archived = repo?.archived ?? false,
                Disabled = repo?.disabled ?? false
            };
        }
        catch (Exception ex) when (ex.Message.Contains("422"))
        {
            // Repository likely already exists
            Logger.LogWarning($"Repository {repoName} already exists");
            return await GetRepositoryAsync(organization, repoName, cancellationToken);
        }
    }

    public async Task<GitHubTeamInfo?> CreateTeamAsync(string organization, string teamName, string? description = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if team exists first
            var existingTeam = await GetTeamAsync(organization, teamName, cancellationToken);
            if (existingTeam != null)
            {
                Logger.LogWarning($"Team {teamName} already exists");
                return existingTeam;
            }

            var body = new
            {
                name = teamName,
                description,
                privacy = "closed",
                permission = "push"
            };

            var team = await InvokeApiAsync<GitHubTeamInfo>($"orgs/{organization}/teams", HttpMethod.Post, body, cancellationToken);
            if (team != null)
            {
                Logger.LogSuccess($"Created GitHub team: {teamName}");
            }
            return team;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create GitHub team: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<bool> AddTeamMemberAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { role = _defaultTeamMemberRole };

            var response = await InvokeApiAsync<object>(
                $"orgs/{organization}/teams/{teamName}/memberships/{username}",
                HttpMethod.Put,
                body,
                cancellationToken);

            if (response != null)
            {
                Logger.LogSuccess($"Added {username} to team {teamName}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to add user {username} to team {teamName}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<GitHubTeamInfo?> GetTeamAsync(string organization, string teamName, CancellationToken cancellationToken = default)
    {
        try
        {
            var teamSlug = teamName.ToLower().Replace(" ", "-");
            var team = await InvokeApiAsync<GitHubTeamInfo>($"orgs/{organization}/teams/{teamSlug}", HttpMethod.Get, cancellationToken: cancellationToken);
            if (team != null)
            {
                Logger.LogSuccess($"Found team {teamName}");
                team.OrganizationName = organization;
                return team;
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get team info for {teamName}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<GitHubRepositoryInfo?> GetRepositoryAsync(string organization, string repoName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InvokeApiAsync<GitHubRepositoryInfo>($"repos/{organization}/{repoName}", HttpMethod.Get, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get repository info for {repoName}: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// Sets the permission level for a team on a specific repository.
    /// </summary>
    /// <param name="organization">The GitHub organization name.</param>
    /// <param name="teamName">The name of the team whose permissions are being set.</param>
    /// <param name="repoName">The name of the repository to set permissions for.</param>
    /// <param name="permission">The permission level to set. Default is "push". Valid values are: "pull", "push", "admin", "maintain", "triage".</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if the permission was successfully set, false otherwise.</returns>
    /// <exception cref="Exception">Thrown when the operation fails.</exception>
    public async Task<bool> SetTeamRepositoryPermissionAsync(string organization, string teamName, string repoName, string permission = "push", CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { permission };
            var teamSlug = teamName.ToLower().Replace(" ", "-");
            var response = await InvokeApiAsync<Boolean>(
                $"orgs/{organization}/teams/{teamSlug}/repos/{organization}/{repoName}",
                HttpMethod.Put,
                body,
                cancellationToken);

            if (response)
            {
                Logger.LogSuccess($"Granted {permission} access to team {teamName} for repository {repoName}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to set team repository access: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<GitHubTeamRepositoryPermission?> GetTeamRepositoryPermissionAsync(string organization, string teamName, string repoName, CancellationToken cancellationToken = default)
    {
        try
        {
            // GitHub API requires team slug - convert name to slug format
            var teamSlug = teamName.ToLower().Replace(" ", "-");

            var response = await InvokeApiAsync<dynamic>(
                $"orgs/{organization}/teams/{teamSlug}/repos/{organization}/{repoName}",
                HttpMethod.Get,
                cancellationToken: cancellationToken);

            if (response != null)
            {
                var permission = response.GetProperty("permission").GetString();
                Logger.LogInfo($"Team {teamName} has {permission} permission on repository {repoName}");
                return new GitHubTeamRepositoryPermission
                {
                    RepositoryName = repoName,
                    Permission = permission,
                    Allowed = true
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("404"))
            {
                Logger.LogInfo($"Team {teamName} has no access to repository {repoName}");
                return new GitHubTeamRepositoryPermission
                {
                    RepositoryName = repoName,
                    Permission = "none",
                    Allowed = false
                };
            }
            Logger.LogError($"Failed to get team repository access: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<bool> SetDefaultBranchAsync(string organization, string repoName, string branchName, CancellationToken cancellationToken = default)
{
    try
    {
        // First check if branch exists
        var branchExists = await InvokeApiAsync<object>(
            $"repos/{organization}/{repoName}/branches/{branchName}",
            HttpMethod.Get,
            cancellationToken: cancellationToken);

        if (branchExists == null)
        {
            Logger.LogError($"Cannot set default branch: Branch '{branchName}' does not exist in repository {repoName}");
            return false;
        }

        var body = new { default_branch = branchName };

        var response = await InvokeApiAsync<object>(
            $"repos/{organization}/{repoName}",
            HttpMethod.Patch,
            body,
            cancellationToken);

        if (response != null)
        {
            Logger.LogSuccess($"Set default branch to {branchName} for repository {repoName}");
            return true;
        }
        return false;
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to set default branch: {ex.Message}", ex);
        throw;
    }
}

    /// <summary>
    /// Gets the default branch of a repository in the specified GitHub organization.
    /// </summary>
    /// <param name="organization">The GitHub organization name.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>The name of the default branch.</returns>
    public async Task<string> GetDefaultBranchAsync(string organization, string repository)
    {
        var repoInfo = await InvokeApiAsync<GitHubRepository>(
            $"repos/{organization}/{repository}",
            HttpMethod.Get);

        return repoInfo?.default_branch ?? throw new Exception("Default branch information is missing.");
    }

    /// <summary>
    /// Updates the default branch of a repository in the specified GitHub organization.
    /// </summary>
    /// <param name="organization">The GitHub organization name.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="newDefaultBranch">The new default branch name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateDefaultBranchAsync(string organization, string repository, string newDefaultBranch)
    {
        var payload = new { default_branch = newDefaultBranch };

        var response = await InvokeApiAsync<object>(
            $"repos/{organization}/{repository}",
            HttpMethod.Patch,
            payload);

        if (response == null)
        {
            throw new Exception($"Failed to update default branch for repository '{repository}' in organization '{organization}'.");
        }
    }

    public async Task<bool> IsRepositoryEmptyAsync(string organization, string repoName, CancellationToken cancellationToken = default)
    {
        try
        {
            var branches = await InvokeApiAsync<List<object>>($"repos/{organization}/{repoName}/branches", HttpMethod.Get, cancellationToken: cancellationToken);
            return branches?.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to check if repository is empty: {ex.Message}", ex);
            return true;
        }
    }

    /// <summary>
    /// Checks if a GitHub repository has no commits (is empty)
    /// </summary>
    /// <param name="organization">GitHub organization name</param>
    /// <param name="repoName">Repository name</param>
    /// <returns>True if the repository is empty, false otherwise</returns>
    public async Task<bool> IsRepositoryEmptyAsync(string organization, string repoName)
    {
        try
        {
            // Get commits with a limit of 1 to check if the repository has any commits
            var commits = await InvokeApiAsync<List<object>>($"repos/{organization}/{repoName}/commits?per_page=1", HttpMethod.Get);
            
            // If the returned array is empty, the repository has no commits
            return commits == null || commits.Count == 0;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Repository does not exist or is empty
                return true;
            }
            
            Logger.LogWarning($"Failed to get commits info for {organization}/{repoName}: {ex.Message}");
            // If we can't check, assume it's not empty to be safe
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error checking if repository is empty: {ex.Message}");
            if (ex.Message.Contains("409"))
            {
                // Repository does not exist or is empty
                return true;
            }
            // If there's an error, assume it's not empty to be safe
            return false;
        }
    }

    public async Task<string> GetTeamMembershipStateAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        try
        {
            var membership = await GetTeamMembershipAsync(organization, teamName, username, cancellationToken);
            return membership.State!;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get team membership state: {ex.Message}", ex);
            return "";
        }
    }

    public async Task<GitHubMigrationResult> MigrateTeamMembersAsync(
        string organization,
        string teamName,
        List<MigrationTeamMember> members,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (members == null || members.Count == 0)
            {
                throw new ArgumentException("No members provided for migration");
            }

            if (string.IsNullOrWhiteSpace(organization))
            {
                throw new ArgumentException("Organization name cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(teamName))
            {
                throw new ArgumentException("Team name cannot be empty");
            }

            Logger.LogInfo($"Migrating users to GitHub team: {teamName}");

            // Verify team exists before proceeding
            var teamInfo = await GetTeamAsync(organization, teamName, cancellationToken);
            if (teamInfo == null)
            {
                throw new Exception($"Team {teamName} does not exist in organization {organization}");
            }

            Logger.LogInfo($"Found {members.Count} members to migrate");

            var migrationResult = new GitHubMigrationResult
            {
                Status = MigrationStatus.Completed,
                MigrationDate = DateTime.UtcNow,
                Members = new List<GitHubMigratedMember>()
            };

            foreach (var member in members)
            {
                try
                {
                    if (member.IsGroup)
                    {
                        Logger.LogWarning($"Skipping group: {member.DisplayName}");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(member.GitHubUserName))
                    {
                        await AddTeamMemberAsync(organization, teamName, member.GitHubUserName, cancellationToken);
                        var status = MigrationStatus.InProgress;
                        var state = await GetTeamMembershipStateAsync(organization, teamName, member.GitHubUserName, cancellationToken);
                        if (state == "active")
                        {
                            status = MigrationStatus.Completed;
                        }
                        else if (state == "pending")
                        {
                            status = MigrationStatus.Pending;
                        }
                        else
                        {
                            status = MigrationStatus.Failed;
                        }

                        migrationResult.Members.Add(new GitHubMigratedMember
                        {
                            AdoEmail = member.Email,
                            GitHubUsername = member.GitHubUserName,
                            Status = status
                        });
                    }

                }
                catch (Exception ex)
                {
                    migrationResult.Members.Add(new GitHubMigratedMember
                    {
                        AdoEmail = member.Email ?? member.UniqueName,
                        Status = MigrationStatus.Failed,
                        Error = ex.Message
                    });
                }
            }

            var successCount = migrationResult.Members.Count(m => m.Status == MigrationStatus.Completed);
            Logger.LogInfo($"Migration completed. Successfully migrated {successCount} out of {members.Count} members");
            if (successCount == members.Count)
            {
                migrationResult.Status = MigrationStatus.Completed;
            }
            else if (successCount > 0)
            {
                migrationResult.Status = MigrationStatus.PartiallyCompleted;
            }
            else
            {
                migrationResult.Status = MigrationStatus.Pending;
            }

            return migrationResult;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Team member migration failed: {ex.Message}", ex);
            return new GitHubMigrationResult
            {
                Status =MigrationStatus.Failed,
                Error = ex.Message,
                MigrationDate = DateTime.UtcNow,
                Members = new List<GitHubMigratedMember>()
            };
        }
    }

    public async Task<List<SAMLUserIdentity>> GetSamlIdentitiesAsync(string organization, string continuationToken, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Fetching SAML identities for organization {organization}");
            List<SAMLUserIdentity> result = new List<SAMLUserIdentity>();
            var query = @"
                        query {
                          organization(login: ""$org"") {
                            samlIdentityProvider {
                                id
                                ssoUrl
                                issuer
                              externalIdentities( first: 100, after: ""$continuationToken"") {
                                totalCount
                                pageInfo {
                                    hasNextPage
                                    endCursor
                                }
                                edges {
                                  node {
                                    user {
                                      id
                                      login
                                      name
                                      email
                                    }
                                    samlIdentity {
                                      nameId
                                    }
                                  }
                                }
                              }
                            }
                          }
                        }";

            query = query.Replace("$org", organization);
            query = query.Replace("$continuationToken", continuationToken);

            var response = await InvokeGraphQLAsync<GraphQLSAMLIdentityResponse>(query, cancellationToken);

            if (response?.organization?.samlIdentityProvider?.externalIdentities?.edges == null)
            {
                return new List<SAMLUserIdentity>();
            }

            // Always add current page results first
            result.AddRange(
                response.organization.samlIdentityProvider.externalIdentities.edges
                    .Where(n => n.node?.user != null && n.node.samlIdentity != null)
                    .Select(n => new SAMLUserIdentity
                    {
                        SamlIdentity = n.node?.samlIdentity!.nameId!,
                        Login = n.node?.user?.login!,
                        Name = n.node?.user?.name?.ToString() ?? string.Empty,
                        Email = n.node?.user?.email ?? n.node?.samlIdentity?.nameId
                    })
                    .ToList());

            // Then fetch next page if available
            if (response.organization.samlIdentityProvider.externalIdentities.pageInfo.hasNextPage)
            {
                var nextPage = response.organization.samlIdentityProvider.externalIdentities.pageInfo.endCursor;
                var nextResponse = await GetSamlIdentitiesAsync(organization, nextPage!, cancellationToken);
                result.AddRange(nextResponse);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to fetch SAML identities: {ex.Message}", ex);
            return new List<SAMLUserIdentity>();
        }
    }
    public async Task<List<GitHubUser>> GetOrgUsersAsync(string organization, string continuationToken, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Fetching users for organization {organization}");
            List<GitHubUser> result = new List<GitHubUser>();
            var query = @"  
                        query {
                          organization(login: ""$org"") {
                            membersWithRole(first: 100, after: ""$continuationToken"") {
                            totalCount
                            pageInfo {
                                hasNextPage
                                endCursor
                            }
                            edges {
                                node {
                                id
                                login
                                name
                                email
                                }
                                role
                            }
                            }
                        }          
                        }
                        }";

            query = query.Replace("$org", organization);
            query = query.Replace("$continuationToken", continuationToken);

            var response = await InvokeGraphQLAsync<GraphQLOrgUsersResponse>(query, cancellationToken);

            if (response?.organization?.membersWithRole?.edges == null)
            {
                return new List<GitHubUser>();
            }

            // Always add current page results first
            result.AddRange(
                response.organization.membersWithRole.edges
                    .Where(n => n.node?.id != null)
                    .Select(n => new GitHubUser
                    {
                        Id = n.node?.id,
                        OrgRole = n.role,
                        Email = n.node?.email,
                        Login = n.node?.login,
                        Name = n.node?.name?.ToString() ?? string.Empty,

                    })
                    .ToList());

            // Then fetch next page if available
            if (response.organization.membersWithRole.pageInfo.hasNextPage)
            {
                var nextPage = response.organization.membersWithRole.pageInfo.endCursor;
                var nextResponse = await GetOrgUsersAsync(organization, nextPage!, cancellationToken);
                result.AddRange(nextResponse);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to fetch organization users: {ex.Message}", ex);
            return new List<GitHubUser>();
        }
    }

    public async Task<bool> ValidateOrganizationAccessAsync(string organization)
    {
        try
        {
            var response = await InvokeApiAsync<object>($"orgs/{organization}", HttpMethod.Get);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RepositoryExistsAsync(string organization, string repoName)
    {
        try
        {
            var response = await InvokeApiAsync<object>($"repos/{organization}/{repoName}", HttpMethod.Get);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task CreateRepositoryAsync(string organization, string repoName, bool isPrivate)
    {
        var payload = new
        {
            name = repoName,
            @private = isPrivate,
            auto_init = false
        };

        await InvokeApiAsync<object>($"orgs/{organization}/repos", HttpMethod.Post, payload);
    }

    public async Task<bool> TeamExistsAsync(string organization, string teamName)
    {
        try
        {
            // GitHub API requires team slug - convert name to slug format
            var teamSlug = teamName.ToLower().Replace(" ", "-");
            var response = await InvokeApiAsync<object>($"orgs/{organization}/teams/{teamSlug}", HttpMethod.Get);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task CreateTeamAsync(string organization, string teamName)
    {
        var payload = new
        {
            name = teamName,
            privacy = "closed"
        };

        await InvokeApiAsync<object>($"orgs/{organization}/teams", HttpMethod.Post, payload);
    }

    public async Task AddTeamMemberAsync(string organization, string teamName, string username)
    {
        // GitHub API requires team slug - convert name to slug format
        var teamSlug = teamName.ToLower().Replace(" ", "-");

        await InvokeApiAsync<object>(
            $"orgs/{organization}/teams/{teamSlug}/memberships/{username}",
            HttpMethod.Put,
            new { role = _defaultTeamMemberRole });
    }

    public async Task<bool> IsSsoActiveAsync(string organization, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Checking SSO status for organization {organization}");
            var query = @"
                query {
                    organization(login: ""$org"") {
                        samlIdentityProvider {
                            samlIdentityProvider {
                                id
                                ssoUrl
                                issuer
                                }
                            }
                        }
                    }
                }";

            query = query.Replace("$org", organization);


            var response = await InvokeGraphQLAsync<GraphQLSAMLIdentityResponse>(query, cancellationToken);

            return response?.organization?.samlIdentityProvider != null;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to check SSO status: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> HasSamlEnabledAsync(string organization, CancellationToken cancellationToken = default)
    {
        try
        {
            return await IsSsoActiveAsync(organization, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to check SAML status: {ex.Message}", ex);
            return false;
        }
    }



    /// <summary>
    /// Checks if a user is a member of a GitHub team
    /// </summary>
    /// <param name="organization">The GitHub organization name</param>
    /// <param name="teamName">The GitHub team name</param>
    /// <param name="username">The GitHub username to check</param>
    /// <returns>True if the user is a member of the team, false otherwise</returns>
    public async Task<bool> TeamMembershipIsActiveAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        var teamMembershipState = await GetTeamMembershipAsync(organization, teamName, username, cancellationToken);

        return teamMembershipState.State == "active";

    }

    /// <summary>
    /// Retrieves the role of a user in a specified GitHub team.
    /// </summary>
    /// <param name="organization">The GitHub organization name.</param>
    /// <param name="teamName">The GitHub team name.</param>
    /// <param name="username">The GitHub username to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The role of the user in the team.</returns>
    /// <exception cref="Exception">Thrown if the user is not a member of the team or if an error occurs while checking membership.</exception>
    public async Task<string> GetTeamMembershipRoleAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        var teamMembershipState = await GetTeamMembershipAsync(organization, teamName, username, cancellationToken);

        // Ensured safe return for teamMembershipState.Role
        return teamMembershipState?.Role ?? string.Empty;
    }

    public async Task<GitHubTeamMembershipState> GetTeamMembershipAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeApiAsync<GitHubTeamMembershipState>(
                $"orgs/{organization}/teams/{teamName}/memberships/{username}",
                HttpMethod.Get,
                cancellationToken: cancellationToken);

            if (response != null)
            {
                return response;
            }
            return new GitHubTeamMembershipState
            {
                Username = username,
                State = "not_found",
                Role = ""
            };


        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to check team membership for user {username} in team {teamName}: {ex.Message}");
            return new GitHubTeamMembershipState
            {
                Username = username,
                State = "error",
                Role = ""
            };
        }
    }

    /// <summary>
    /// Gets the Personal Access Token used for authentication
    /// </summary>
    /// <returns>The PAT value</returns>
    public string GetPat() => _token;

    /// <summary>
    /// Updates the Personal Access Token used for authentication and refreshes authentication headers
    /// </summary>
    /// <param name="token">The new PAT value to use</param>
    public void UpdatePat(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be empty", nameof(token));

        _token = token.Trim();

        // Update authentication headers
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    /// <summary>
    /// Gets the team ID for a given team name in an organization
    /// </summary>
    /// <param name="organization">The GitHub organization name</param>
    /// <param name="teamName">The name of the team to look up</param>
    /// <param name="cancellationToken">Optional cancellation token to cancel the operation</param>
    /// <returns>The team ID as a string if found, null otherwise</returns>
    private async Task<string?> GetTeamIdAsync(string organization, string teamName, CancellationToken cancellationToken = default)
    {
        var team = await GetTeamAsync(organization, teamName, cancellationToken);
        if (team == null)
        {
            Logger.LogWarning($"Team {teamName} not found in organization {organization}");
            return null;
        }
      
        return team?.Id.ToString();
    }

    /// <summary>
    /// Sets a user as a team administrator (maintainer) in a GitHub team.
    /// </summary>
    /// <param name="org">The GitHub organization name.</param>
    /// <param name="team">The name of the team.</param>
    /// <param name="username">The GitHub username to be set as admin.</param>
    /// <returns>
    /// A boolean task that returns true if the user was successfully set as admin,
    /// false if the operation failed.
    /// </returns>
    public async Task<bool> SetTeamAdminAsync(string org, string team, string username)
    {
        var teamSlug = team.ToLower().Replace(" ", "-");
        var body = new { role = "maintainer" };
        
        var response = await InvokeApiAsync<object>(
            $"orgs/{org}/teams/{teamSlug}/memberships/{username}",
            HttpMethod.Put,
            body);

        return response != null;
    }
    /// <summary>
    /// Gets the latest commit hash from a specific branch in a GitHub repository
    /// </summary>
    /// <param name="organization">GitHub organization name</param>
    /// <param name="repoName">Repository name</param>
    /// <param name="branchName">Branch name to check</param>
    /// <returns>Latest commit hash or null if not found</returns>
    public async Task<string?> GetBranchLatestCommitAsync(string organization, string repoName, string branchName)
    {
        try
        {
            // Get branch information from the GitHub API
            var url = $"repos/{organization}/{repoName}/branches/{branchName}";
            var response = await _httpClient.GetAsync(BuildUrl(url));
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning($"Failed to get branch info: {response.StatusCode}");
                return null;
            }
            
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            
            // Extract commit SHA from the response
            if (doc.RootElement.TryGetProperty("commit", out var commit) &&
                commit.TryGetProperty("sha", out var sha))
            {
                return sha.GetString();
            }
            
            return null;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning($"Failed to get branch info for {organization}/{repoName}/{branchName}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error getting latest GitHub commit: {ex.Message}");
            return null;
        }
    }        
        /// <summary>
        /// Logs GitHub API rate limit information from response headers
        /// </summary>
        /// <param name="response">The HTTP response message containing rate limit headers</param>
        private void LogRateLimitInfo(HttpResponseMessage response)
        {
            try
            {
                // Check if rate limit headers are present
                string? limit = null;
                string? remaining = null;
                string? resetTimestamp = null;
                string? used = null;
                string? resource = "core"; // Default resource type

                if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues))
                {
                    limit = limitValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
                {
                    remaining = remainingValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
                {
                    resetTimestamp = resetValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-Used", out var usedValues))
                {
                    used = usedValues.FirstOrDefault();
                }
                
                // Check for resource-specific rate limit headers
                if (response.Headers.TryGetValues("X-RateLimit-Resource", out var resourceValues))
                {
                    resource = resourceValues.FirstOrDefault() ?? resource;
                }

                // Check if we're hitting search API rate limits
                string? searchLimit = null;
                string? searchRemaining = null;
                string? searchReset = null;
                
                if (response.Headers.TryGetValues("X-RateLimit-Search-Limit", out var searchLimitValues))
                {
                    searchLimit = searchLimitValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-Search-Remaining", out var searchRemainingValues))
                {
                    searchRemaining = searchRemainingValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-Search-Reset", out var searchResetValues))
                {
                    searchReset = searchResetValues.FirstOrDefault();
                }

                // Check if we're hitting GraphQL API rate limits
                string? graphqlLimit = null;
                string? graphqlRemaining = null;
                string? graphqlReset = null;
                
                if (response.Headers.TryGetValues("X-RateLimit-GraphQL-Limit", out var graphqlLimitValues))
                {
                    graphqlLimit = graphqlLimitValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-GraphQL-Remaining", out var graphqlRemainingValues))
                {
                    graphqlRemaining = graphqlRemainingValues.FirstOrDefault();
                }
                
                if (response.Headers.TryGetValues("X-RateLimit-GraphQL-Reset", out var graphqlResetValues))
                {
                    graphqlReset = graphqlResetValues.FirstOrDefault();
                }

                // If we have rate limit info, log it
                if (!string.IsNullOrEmpty(limit) && !string.IsNullOrEmpty(remaining))
                {
                    // Convert reset timestamp to readable time if available
                    string resetTimeFormatted = "unknown";
                    if (!string.IsNullOrEmpty(resetTimestamp) && long.TryParse(resetTimestamp, out var resetEpoch))
                    {
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetEpoch).LocalDateTime;
                        resetTimeFormatted = resetTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    
                    Logger.LogInfo($"GitHub API Rate Limit ({resource}): {remaining}/{limit} remaining. Used: {used ?? "unknown"}. Resets at: {resetTimeFormatted}");
                    
                    // Add warning if remaining requests are low
                    if (int.TryParse(remaining, out var remainingCount) && remainingCount < 100)
                    {
                        Logger.LogWarning($"GitHub API rate limit is getting low: {remainingCount} requests remaining for {resource} operations!");
                    }
                }

                // Log search API limits if available
                if (!string.IsNullOrEmpty(searchLimit) && !string.IsNullOrEmpty(searchRemaining))
                {
                    string searchResetFormatted = "unknown";
                    if (!string.IsNullOrEmpty(searchReset) && long.TryParse(searchReset, out var searchResetEpoch))
                    {
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(searchResetEpoch).LocalDateTime;
                        searchResetFormatted = resetTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    
                    Logger.LogInfo($"GitHub Search API Rate Limit: {searchRemaining}/{searchLimit} remaining. Resets at: {searchResetFormatted}");
                }

                // Log GraphQL API limits if available
                if (!string.IsNullOrEmpty(graphqlLimit) && !string.IsNullOrEmpty(graphqlRemaining))
                {
                    string graphqlResetFormatted = "unknown";
                    if (!string.IsNullOrEmpty(graphqlReset) && long.TryParse(graphqlReset, out var graphqlResetEpoch))
                    {
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(graphqlResetEpoch).LocalDateTime;
                        graphqlResetFormatted = resetTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    
                    Logger.LogInfo($"GitHub GraphQL API Rate Limit: {graphqlRemaining}/{graphqlLimit} remaining. Resets at: {graphqlResetFormatted}");
                }
            }
            catch (Exception ex)
            {
                // Don't let rate limit logging failures affect the main operation
                Logger.LogDebug($"Failed to log rate limit info: {ex.Message}");
            }
        }    
    /// <summary>
    /// Gets the current GitHub API rate limit status including core, search, and graphql limits
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task GetAndLogRateLimitStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo("Querying GitHub API rate limit status...");
            
            // Make a direct call to rate limit endpoint which will return all rate limit info
            var response = await InvokeApiAsync<JsonDocument>("rate_limit", HttpMethod.Get, cancellationToken: cancellationToken);
            
            if (response == null)
            {
                Logger.LogWarning("Failed to retrieve rate limit information.");
                return;
            }
            
            // Parse and log detailed rate limit information
            if (response.RootElement.TryGetProperty("resources", out var resources))
            {
                // Log Core API limits
                if (resources.TryGetProperty("core", out var core))
                {
                    LogRateLimitResource("Core API", core);
                }
                
                // Log Search API limits
                if (resources.TryGetProperty("search", out var search))
                {
                    LogRateLimitResource("Search API", search);
                }
                
                // Log GraphQL API limits
                if (resources.TryGetProperty("graphql", out var graphql))
                {
                    LogRateLimitResource("GraphQL API", graphql);
                }
                
                // Log integration manifest API limits if present
                if (resources.TryGetProperty("integration_manifest", out var integrationManifest))
                {
                    LogRateLimitResource("Integration Manifest API", integrationManifest);
                }
                
                // Log code scanning upload API limits if present
                if (resources.TryGetProperty("code_scanning_upload", out var codeScanningUpload))
                {
                    LogRateLimitResource("Code Scanning Upload API", codeScanningUpload);
                }
                
                // Log SCIM API limits if present
                if (resources.TryGetProperty("scim", out var scim))
                {
                    LogRateLimitResource("SCIM API", scim);
                }
            }
            
            Logger.LogSuccess("Rate limit query completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to query rate limit status: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Helper method to log rate limit information for a specific GitHub API resource
    /// </summary>
    /// <param name="resourceName">Name of the API resource</param>
    /// <param name="resourceElement">JsonElement containing rate limit data</param>
    private void LogRateLimitResource(string resourceName, JsonElement resourceElement)
    {
        try
        {
            int limit = resourceElement.GetProperty("limit").GetInt32();
            int remaining = resourceElement.GetProperty("remaining").GetInt32();
            long resetTimestamp = resourceElement.GetProperty("reset").GetInt64();
            int used = resourceElement.GetProperty("used").GetInt32();
            
            // Convert Unix timestamp to readable date
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp).LocalDateTime;
            
            var percentUsed = limit > 0 ? (used * 100.0 / limit) : 0;
            
            // Format the message
            var message = $"{resourceName} Rate Limit: {remaining}/{limit} remaining " +
                         $"({percentUsed:F1}% used). Resets at: {resetTime:yyyy-MM-dd HH:mm:ss}";
            
            // Use appropriate log level based on remaining capacity
            if (remaining <= 10)
            {
                Logger.LogError(message); // Critical - almost no requests left
            }
            else if (remaining < limit * 0.1)
            {
                Logger.LogWarning(message); // Warning - less than 10% remaining
            }
            else
            {
                Logger.LogInfo(message); // Info - normal levels
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Error parsing rate limit info for {resourceName}: {ex.Message}");
        }

    }
}
