namespace AzureDevOps2GitHubMigrator.AzureDevOps;

using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AzureDevOps2GitHubMigrator.Models;
using AzureDevOps2GitHubMigrator.Utils;
using Polly;
using Polly.Retry;
using AzureDevOps2GitHubMigrator.Models.Ado;

/// <summary>
/// Service for interacting with Azure DevOps APIs and resources.
/// </summary>
public class AzureDevOpsService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _personalAccessToken;
    private readonly string _apiVersion;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    /// <summary>
    /// Initializes a new instance of AzureDevOpsService with retry policy and authentication
    /// </summary>
    /// <param name="httpClient">HTTP client for making API requests</param>
    /// <param name="baseUrl">Base URL of Azure DevOps organization</param>
    /// <param name="personalAccessToken">Personal Access Token for authentication</param>
    /// <param name="apiVersion">Azure DevOps API version to use</param>
    public AzureDevOpsService(HttpClient httpClient, string baseUrl, string personalAccessToken, string adoVersion = "cloud")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl.TrimEnd('/');
        _personalAccessToken = personalAccessToken;
        _apiVersion = Common.GetAdoApiVersion(adoVersion);

        // Setup basic auth with PAT
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .OrResult(response => (int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3, // number of retries
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // exponential backoff: 2, 4, 8 seconds
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    Logger.LogWarning($"Request failed. Retry attempt {retryCount} after {timeSpan.TotalSeconds} seconds. Error: {exception.Exception?.Message ?? exception.Result?.StatusCode.ToString()}");
                }
            );
    }

    /// <summary>
    /// Builds a fully qualified API URL with version parameter
    /// </summary>
    private Uri BuildUrl(string relativeUrl)
    {
        var separator = relativeUrl.Contains("?") ? "&" : "?";
        return new Uri($"{_baseUrl}/{relativeUrl.TrimStart('/')}{separator}api-version={_apiVersion}");
    }

    /// <summary>
    /// Makes an HTTP request to Azure DevOps API with retry policy
    /// </summary>
    /// <typeparam name="T">Expected response type</typeparam>
    /// <param name="relativeUrl">API endpoint relative URL</param>
    /// <param name="method">HTTP method to use</param>
    /// <param name="body">Optional request body</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response of type T</returns>
    private async Task<T?> InvokeAsync<T>(string relativeUrl, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
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
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Bad Request: {errorContent}");
            }
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength == 0)
                return default;

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Azure DevOps API call failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tests the connection to Azure DevOps by making a simple API call
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo("Validating Azure DevOps connection...");
            await InvokeAsync<object>($"_apis/projects", HttpMethod.Get, cancellationToken: cancellationToken);
            Logger.LogSuccess("Azure DevOps connection validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to connect to Azure DevOps: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Retrieves a list of repositories for a given project
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="defaultBranch">Default branch name to use if not specified</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of repositories in the project</returns>
    public async Task<List<Repository>> GetRepositoriesAsync(string projectName, string defaultBranch = "main", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        Logger.LogInfo($"Getting repositories for project: {projectName}");
        var repos = new List<Repository>();

        // Get Git repositories
        var response = await InvokeAsync<ApiResponse<Repository>>($"{projectName}/_apis/git/repositories", HttpMethod.Get, cancellationToken: cancellationToken);
        if (response?.Value != null)
        {
            foreach (var gitRepo in response.Value)
            {
                if (gitRepo.Name == null) continue;

                gitRepo.Url = $"{_baseUrl}/{projectName}/_git/{gitRepo.Name}";
                gitRepo.ProjectName = projectName;
                gitRepo.RepositoryType = "git";

                var branches = await GetRepoBranchesAsync(projectName, gitRepo.Name, cancellationToken);

                if (branches.Count > 0)
                {
                    gitRepo.DefaultBranch = branches.FirstOrDefault(b => b.IsBaseVersion)?.Name;
                    if (string.IsNullOrEmpty(gitRepo.DefaultBranch))
                    {
                        gitRepo.DefaultBranch = !string.IsNullOrEmpty(gitRepo.DefaultBranch) ?
                            gitRepo.DefaultBranch : defaultBranch;
                    }
                    gitRepo.Branches = branches;
                    gitRepo.DefaultBranch = gitRepo.DefaultBranch?.Replace("refs/heads/", "");
                }
                repos.Add(gitRepo);
            }
        }

        // Check for TFVC
        try
        {
            var tfvcRootPath = $"$/{Uri.EscapeDataString(projectName)}";
            var tfvcResponse = await InvokeAsync<ApiResponse<Branch>>($"{projectName}/_apis/tfvc/items?scopePath={tfvcRootPath}", HttpMethod.Get, cancellationToken: cancellationToken);

            if (tfvcResponse?.Value?.Count > 0)
            {
                Logger.LogInfo($"Found TFVC repository in project {projectName}");
                var branches = await GetTfvcBranchesAsync(projectName, cancellationToken);
                repos.Add(new Repository
                {
                    Id = $"tfvc_{projectName}",
                    Name = projectName,
                    Url = $"{_baseUrl}/{projectName}",
                    IsDisabled = false,
                    IsInMaintenance = false,
                    Size = 0,
                    RepositoryType = "tfvc",
                    DefaultBranch = branches.Where(b => b.IsDefault == true).FirstOrDefault()?.Name,
                    Branches = branches
                });
            }
        }
        catch
        {
            Logger.LogInfo($"No TFVC repository found in project {projectName}");
        }

        return repos;
    }

    /// <summary>
    /// Retrieves a list of branches for a Git repository in Azure DevOps
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="repositoryName">Name of the Git repository</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of branches in the repository</returns>
    /// <remarks>
    /// Returns an empty list if the repository is inaccessible or if permission is denied (VS403403)
    /// </remarks>
    private async Task<List<Branch>> GetRepoBranchesAsync(string projectName, string repositoryName, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Getting branches for repository {repositoryName} in project {projectName}...");
            var response = await InvokeAsync<ApiResponse<Branch>>($"{projectName}/_apis/git/repositories/{repositoryName}/stats/branches", HttpMethod.Get, cancellationToken: cancellationToken);

            var branches = response?.Value ?? new List<Branch>();

            Logger.LogSuccess($"Found {branches.Count} branches in repository {repositoryName}");
            return branches;
        }
        catch (Exception ex) when (ex.Message.Contains("VS403403"))
        {
            return new List<Branch>();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get branches for repository {repositoryName}: {ex.Message}");
            return new List<Branch>();
        }
    }


    /// <summary>
    /// Retrieves a list of branches for a TFVC repository in Azure DevOps
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of branches in the TFVC repository</returns>
    /// <remarks>
    /// This method first gets the root branch for the project and then searches for the main branch.
    /// It ensures the default branch is properly marked in the returned list.
    /// </remarks>
    public async Task<List<Branch>> GetTfvcBranchesAsync(string projectName, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInfo($"Getting TFVC root branch for project: {projectName}");
            var response = await InvokeAsync<ApiResponse<TfvcBranch>>($"{projectName}/_apis/tfvc/branches?includeParent=true&includeChildren=true", HttpMethod.Get, cancellationToken: cancellationToken);
            var result = new List<Branch>();

            if (response?.Value == null)
                return result;

            foreach (var tfvcBranch in response.Value)
            {
                if (tfvcBranch.path != null && tfvcBranch.path.StartsWith($"$/{projectName}", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new Branch
                    {
                        Name = tfvcBranch.path,
                        IsBaseVersion = true,
                        IsDefault = true
                    });
                }
            }

            //Search main branch
            response = await InvokeAsync<ApiResponse<TfvcBranch>>($"_apis/tfvc/branches?includeParent=true&includeChildren=true", HttpMethod.Get, cancellationToken: cancellationToken);
            if (response?.Value == null)
                return result;

            // Find the default branch for the project starting with the project root path
            var defaultBranch = response?.Value?.FirstOrDefault(branch => 
                branch.path?.StartsWith($"$/{projectName}", StringComparison.OrdinalIgnoreCase) ?? false);

            if (defaultBranch != null)
            {
                //update branch with name as default branch

                var defBranch = result.FirstOrDefault(b => string.Equals(b.Name, defaultBranch.path, StringComparison.OrdinalIgnoreCase));
                if (defBranch == null)
                {
                    result.Add(new Branch
                    {
                        Name = defaultBranch.path,
                        IsBaseVersion = true,
                        IsDefault = true
                    });
                }
                else
                {
                    defBranch.IsDefault = true;
                    defBranch.IsBaseVersion = true;
                }


      
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get TFVC root branch: {ex.Message}", ex);
            return new List<Branch>();

        }
    }

    /// <summary>
    /// Retrieves a list of teams for a given project
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="includeMembers">Whether to include team members in the response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of teams in the project</returns>
    public async Task<List<AdoTeam>> GetTeamsAsync(string projectName, bool includeMembers = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        var teams = new List<AdoTeam>();
        var response = await InvokeAsync<ApiResponse<AdoTeam>>($"_apis/projects/{projectName}/teams", HttpMethod.Get, cancellationToken: cancellationToken);

        if (response?.Value == null)
            return teams;

        foreach (var adoTeam in response.Value.Where(t => !string.IsNullOrWhiteSpace(t.Name)))
        {
            adoTeam.Url = $"{_baseUrl}/{projectName}/_settings/teams/{adoTeam.Name}";
            adoTeam.ProjectName = projectName;
            if (includeMembers && adoTeam.Name != null)
            {
                adoTeam.Members = await GetTeamMembersAsync(projectName, adoTeam.Name, cancellationToken);
            }

            teams.Add(adoTeam);
        }

        if (teams.Count == 0)
        {
            Logger.LogWarning("No valid teams found to migrate");
        }
        else
        {
            Logger.LogSuccess($"Found {teams.Count} valid teams to migrate");
        }

        return teams;
    }

    /// <summary>
    /// Retrieves a list of members for a given team
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="teamName">Name of the team</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of team members</returns>
    public async Task<List<AdoTeamMember>> GetTeamMembersAsync(string projectName, string teamName, CancellationToken cancellationToken = default)
    {
        var members = new List<AdoTeamMember>();
        var encodedTeamName = Uri.EscapeDataString(teamName);
        var response = await InvokeAsync<ApiResponse<AdoTeamMember>>($"_apis/projects/{projectName}/teams/{encodedTeamName}/members", HttpMethod.Get, cancellationToken: cancellationToken);

        Logger.LogInfo($"Found {response?.Value?.Count ?? 0} members in team {teamName}");

        if (response?.Value == null)
            return members;

        foreach (var adoMember in response.Value)
        {
            members.Add(adoMember);
        }

        return members;
    }

    /// <summary>
    /// Retrieves a list of team members across multiple projects
    /// </summary>
    /// <param name="projectNames">Optional comma-separated list of project names. If null or empty, gets from all projects</param>
    /// <param name="includeInactive">Whether to include inactive users</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of team members</returns>
    public async Task<List<AdoTeamMember>> GetTeamMembersAsync(string? projectNames = null, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await ExtractUsersAsync(projectNames, cancellationToken);
            if (!includeInactive)
            {
                users = users.Where(u => u.Identity?.IsEnabled ?? false).ToList();
            }
            return users;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get team members: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a list of projects in the Azure DevOps organization
    /// </summary>
    /// <param name="skip">Number of projects to skip</param>
    /// <param name="top">Maximum number of projects to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of projects</returns>
    public async Task<List<AdoProject>> GetProjectsAsync(int skip = 0, int top = 1000, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new List<AdoProject>();
            Logger.LogInfo("Getting Azure DevOps projects...");
            var url = $"_apis/projects?$skip={skip}&$top={top}";

            var response = await InvokeAsync<ApiResponse<AdoProject>>(url, HttpMethod.Get, cancellationToken: cancellationToken);
            if (response?.Value == null || response.Value.Count == 0)
            {
                Logger.LogWarning("No projects found");
                return result;
            }
            Logger.LogSuccess($"Found {response.Value.Count} projects");
            foreach (var project in response?.Value ?? Enumerable.Empty<AdoProject>())
            {
                project.Url = $"{_baseUrl}/{project.Name}";
                project.AdoOrganization = new Uri(_baseUrl).Segments.Last().TrimEnd('/');
                result.Add(project);
            }

            return result;

        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get Azure DevOps projects: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Retrieves details of a specific project
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="includeCapabilities">Whether to include project capabilities in the response</param>
    /// <param name="includeRepos">Whether to include repositories in the response</param>
    /// <param name="includeTeams">Whether to include teams in the response</param>
    /// <param name="includeTeamMembers">Whether to include team members in the response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Project details</returns>
    public async Task<AdoProject?> GetProjectAsync(string projectName, bool includeCapabilities = false, bool includeRepos = false, bool includeTeams = false, bool includeTeamMembers = false, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(projectName);
            Logger.LogInfo($"Getting Azure DevOps project: {projectName}");
            var url = $"_apis/projects/{Uri.EscapeDataString(projectName)}";
            if (includeCapabilities)
            {
                url += "?includeCapabilities=true";
            }

            var project = await InvokeAsync<AdoProject>(url, HttpMethod.Get, cancellationToken: cancellationToken);
            if (project?.Name == null)
            {
                Logger.LogWarning($"Project '{projectName}' not found");
                return null;
            }
            project.AdoOrganization = DevOpsOrganization();
            project.Url = $"{_baseUrl}/{project.Name}";
            Logger.LogSuccess($"Found project: {project.Name}");

            if (includeRepos)
            {
                project.Repos = await GetRepositoriesAsync(project.Name, cancellationToken: cancellationToken);
            }

            if (project.Repos.Count > 0 && includeTeams)
            {
                project.Teams = await GetTeamsAsync(project.Name, includeTeamMembers, cancellationToken);
            }

            return project;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get Azure DevOps project '{projectName}': {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Extracts a list of unique users from specified projects
    /// </summary>
    /// <param name="projectNames">Comma-separated list of project names or "all" for all projects</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of unique users</returns>
    public async Task<List<AdoTeamMember>> ExtractUsersAsync(string? projectNames = null, CancellationToken cancellationToken = default)
    {
        var uniqueUsers = new Dictionary<string, AdoTeamMember>();

        try
        {
            List<AdoProject> projects = new List<AdoProject>();
            List<String> projectNamesList = new List<string>();
            if (string.IsNullOrWhiteSpace(projectNames) || projectNames.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInfo("Extracting users from all projects...");
                projects = await GetProjectsAsync(cancellationToken: cancellationToken);
                projectNamesList = projects.Select(p => p.Name).ToList()!;

            }
            else if (projectNames.Contains(","))
            {
                projectNamesList = projectNames.Replace("\"", "").Replace("\'", "").Split(',').Select(p => p.Trim()).ToList();
            }
            else
            {
                projectNamesList.Add(projectNames.Replace("\"", "").Replace("\'", "").Trim());
            }
            foreach (var projectName in projectNamesList)
            {
                var teams = await GetTeamsAsync(projectName, true, cancellationToken: cancellationToken);
                foreach (var team in teams ?? Enumerable.Empty<AdoTeam>())
                {
                    foreach (var member in team.Members ?? Enumerable.Empty<AdoTeamMember>())
                    {
                        if (!string.IsNullOrEmpty(member?.Identity?.UniqueName) && !uniqueUsers.ContainsKey(member.Identity.UniqueName))
                        {
                            uniqueUsers[member.Identity.UniqueName] = member;
                        }
                    }
                }
            }

            Logger.LogSuccess($"Found {uniqueUsers.Count} unique users across all projects");
            return uniqueUsers.Values.OrderBy(u => u.Identity?.UniqueName).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to extract users: {ex.Message}", ex);
            if (ex.InnerException != null)
                Logger.LogError($"Details: {ex.InnerException.Message}", ex.InnerException);
            throw;
        }
    }

    /// <summary>
    /// Retrieves the size of a specific repository
    /// </summary>
    /// <param name="projectName">Name of the Azure DevOps project</param>
    /// <param name="repositoryId">ID of the repository</param>
    /// <returns>Size of the repository in bytes</returns>
    public async Task<long> GetRepositorySizeAsync(string projectName, string repositoryId)
    {
        try
        {
            // Get repository statistics
            var url = $"{_baseUrl}/{projectName}/_apis/git/repositories/{repositoryId}/stats?api-version={_apiVersion}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var stats = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (stats.TryGetProperty("repositorySize", out var sizeElement))
            {
                return sizeElement.GetInt64();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get repository size: {ex.Message}", ex);
            return 0;
        }
    }

    /// <summary>
    /// Gets the base URL of the Azure DevOps organization
    /// </summary>
    public string DevOpsUrl() => _baseUrl;

    /// <summary>
    /// Extracts the organization name from the base URL
    /// </summary>
    public string DevOpsOrganization() => new Uri(_baseUrl).Segments.Last().TrimEnd('/');

    /// <summary>
    /// Gets the base URL without modifications
    /// </summary>
    public string DevOpsBaseUrl() => _baseUrl;

    /// <summary>
    /// Gets the Personal Access Token used for authentication
    /// </summary>
    /// <returns>The PAT value</returns>
    public string GetPat() => _personalAccessToken;
}