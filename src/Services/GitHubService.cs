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

public class GitHubService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _token;
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
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult(response => (int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    if (exception.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var resetTime = exception.Result.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
                        if (resetTime != null && DateTimeOffset.TryParse(resetTime, out var reset))
                        {
                            Logger.LogWarning($"Rate limit exceeded. Will reset at: {reset.LocalDateTime}");
                        }
                    }
                    Logger.LogWarning($"Request failed. Retry attempt {retryCount} after {timeSpan.TotalSeconds} seconds. Error: {exception.Exception?.Message ?? exception.Result?.StatusCode.ToString()}");
                }
            );
    }

    private Uri BuildUrl(string relativeUrl) => new Uri($"{_baseUrl}/{relativeUrl.TrimStart('/')}");

    private async Task<T?> InvokeApiAsync<T>(string relativeUrl, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, BuildUrl(relativeUrl));
            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.SendAsync(request, cancellationToken));

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return default;

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength == 0)
                return default;

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"GitHub API call failed after retries: {ex.Message}", ex);
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
            graphQLRequest.Content = JsonContent.Create(request);

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.SendAsync(graphQLRequest, cancellationToken));

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

            var repo = await InvokeApiAsync<GitHubRepositoryInfo>($"orgs/{organization}/repos", HttpMethod.Post, repoData, cancellationToken);
            if (repo != null)
            {
                Logger.LogSuccess($"Successfully created repository: {repoName}");
            }
            return repo;
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
            var teams = await InvokeApiAsync<List<GitHubTeamInfo>>($"orgs/{organization}/teams", HttpMethod.Get, cancellationToken: cancellationToken);
            return teams?.FirstOrDefault(t => t.Name == teamName || t.Slug == teamName.ToLowerInvariant());
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

    public async Task<bool> SetTeamRepositoryPermissionAsync(string organization, string teamName, string repoName, string permission = "push", CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { permission };

            var response = await InvokeApiAsync<object>(
                $"orgs/{organization}/teams/{teamName}/repos/{organization}/{repoName}",
                HttpMethod.Put,
                body,
                cancellationToken);

            if (response != null)
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

    public async Task<bool> SetDefaultBranchAsync(string organization, string repoName, string branchName, CancellationToken cancellationToken = default)
    {
        try
        {
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

    public async Task<bool> IsRepositoryEmptyAsync(string organization, string repoName, CancellationToken cancellationToken = default)
    {
        try
        {
            var branches = await InvokeApiAsync<List<object>>($"repos/{organization}/{repoName}/branches", HttpMethod.Get, cancellationToken: cancellationToken);
            return branches?.Count == 0;
        }
        catch
        {
            return true;
        }
    }

    public async Task<bool> GetTeamMembershipStateAsync(string organization, string teamName, string username, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeApiAsync<GitHubTeamMembershipState>(
                $"orgs/{organization}/teams/{teamName}/memberships/{username}",
                HttpMethod.Get,
                cancellationToken: cancellationToken);

            return response?.State == "active";
        }
        catch
        {
            return false;
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
                Status = "completed",
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

                    var email = member.Email ?? member.UniqueName;
                    Logger.LogInfo($"Processing user: {email}");

                    if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                    {
                        var migratedMember = new GitHubMigratedMember
                        {
                            AdoEmail = email,
                            Status = "failed",
                            Error = "Invalid email format"
                        };
                        migrationResult.Members.Add(migratedMember);
                        continue;
                    }

                    var githubUser = string.Empty;
                    if (!string.IsNullOrWhiteSpace(githubUser))
                    {
                        await AddTeamMemberAsync(organization, teamName, githubUser, cancellationToken);

                        if (await GetTeamMembershipStateAsync(organization, teamName, githubUser, cancellationToken))
                        {
                            migrationResult.Members.Add(new GitHubMigratedMember
                            {
                                AdoEmail = email,
                                GitHubUsername = githubUser,
                                Status = "completed"
                            });
                        }
                        else
                        {
                            throw new Exception("Team membership not active");
                        }
                    }
                    else
                    {
                        migrationResult.Members.Add(new GitHubMigratedMember
                        {
                            AdoEmail = email,
                            Status = "failed",
                            Error = "GitHub user not found"
                        });
                    }
                }
                catch (Exception ex)
                {
                    migrationResult.Members.Add(new GitHubMigratedMember
                    {
                        AdoEmail = member.Email ?? member.UniqueName,
                        Status = "failed",
                        Error = ex.Message
                    });
                }
            }

            var successCount = migrationResult.Members.Count(m => m.Status == "completed");
            Logger.LogInfo($"Migration completed. Successfully migrated {successCount} out of {members.Count} members");

            return migrationResult;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Team member migration failed: {ex.Message}", ex);
            return new GitHubMigrationResult
            {
                Status = "failed",
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
                    .Where(n => n.node.user != null && n.node.samlIdentity != null)
                    .Select(n => new SAMLUserIdentity
                    {
                        SamlIdentity = n.node.samlIdentity.nameId,
                        Login = n.node.user.login,
                        Name = n.node.user.name?.ToString() ?? string.Empty,
                        Email = n.node.user.email ?? n.node.samlIdentity.nameId
                    })
                    .ToList());

            // Then fetch next page if available
            if (response.organization.samlIdentityProvider.externalIdentities.pageInfo.hasNextPage)
            {
                var nextPage = response.organization.samlIdentityProvider.externalIdentities.pageInfo.endCursor;
                var nextResponse = await GetSamlIdentitiesAsync(organization, nextPage, cancellationToken);
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
                    .Where(n => n.node.id != null )
                    .Select(n => new GitHubUser
                    {
                        Id = n.node.id,
                        OrgRole = n.role,
                        Email = n.node.email,
                        Login = n.node.login,
                        Name = n.node.name?.ToString() ?? string.Empty,

                    })
                    .ToList());

            // Then fetch next page if available
            if (response.organization.membersWithRole.pageInfo.hasNextPage)
            {
                var nextPage = response.organization.membersWithRole.pageInfo.endCursor;
                var nextResponse = await GetOrgUsersAsync(organization, nextPage, cancellationToken);
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
}
