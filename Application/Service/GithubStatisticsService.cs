using Application.Model;
using Application.Service.Interface;
using System.Dynamic;
using System.Net.Http.Headers;
using System.Text.Json;
using static Application.Service.GithubStatisticsService;

namespace Application.Service;

public class GithubStatisticsService : IGithubStatisticsService
{
    public event Action<List<RankingItem>> GithubStatisticsUpdated;
    public event Action<TopLongCommit> TopLongCommitUpdated;
    private DateTime sinceDate = DateTime.UtcNow.AddYears(-6);

    private readonly HttpClient client = new HttpClient();

    public GithubStatisticsService()
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        client.Timeout = new TimeSpan(0, 0, 5);
    }

    public async Task<User> GetUser(string ghToken)
    {
        var result = new User();

        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ghToken);

            var response = await client.GetAsync($"https://api.github.com/user");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            result = JsonSerializer.Deserialize<User>(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return result;
    }

    public async Task GetTopLongCommit(User user, string organization, string ghToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", ghToken);

        var repos = await GetRepos(organization);
        repos.AddRange(await GetReposFromUser());
        var mergeCount = new Dictionary<string, int>();

        CommitDetails lastTopCommit = null;

        Console.WriteLine($"Total repos: {repos.Count}");

        var maxDegreeOfParallelism = 100; // Ajuste conforme necessário
        var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync();
            try
            {
                var commits = await GetCommits(user, null, repo);

                if (organization is not null)
                    commits.AddRange(await GetCommits(user, organization, repo));

                Console.WriteLine($"Repo: {repo.name} {commits.Count} Commits");

                foreach (var commit in commits)
                {
                    var commitDetail = await GetCommitDetails(user, organization, repo, commit.sha);

                    if (commitDetail is not null)
                    {
                        commitDetail.repo = repo.name;

                        if (commitDetail?.stats.total > (lastTopCommit?.stats.total ?? 0))
                            lastTopCommit = commitDetail;
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var topLongCommit = new TopLongCommit
        {
            RepoName = lastTopCommit.repo,
            TotalChangedLines = lastTopCommit.stats.total,
            Date = lastTopCommit.commit.author.date
        };

        TopLongCommitUpdated?.Invoke(topLongCommit);
    }

    private async Task<List<Repo>> GetReposFromUser()
    {
        var repos = new List<Repo>();
        var page = 1;

        try
        {
            while (true)
            {
                var response = await client.GetAsync($"https://api.github.com/user/repos?visibility=all&per_page=1000&page={page}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<List<Repo>>(content);
                if (result.Count == 0) break;
                repos.AddRange(result);
                page++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return repos;
    }

    private async Task<List<Repo>> GetRepos(string org)
    {
        var repos = new List<Repo>();
        var page = 1;

        try
        {
            while (true)
            {
                var response = await client.GetAsync($"https://api.github.com/orgs/{org}/repos?per_page=1000&page={page}");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<List<Repo>>(content);
                if (result.Count == 0) break;
                repos.AddRange(result);
                page++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return repos;
    }

    private async Task<List<PullRequest>> GetPullRequests(string org, Repo repo)
    {
        var pulls = new List<PullRequest>();
        var page = 1;

        try
        {
            while (true)
            {
                Console.WriteLine($"calling API for: {repo.name}, Page: {page}");
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{org}/{repo.name}/pulls?state=closed&per_page=100&page={page}"))
                {
                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Fail to get pull requests from: {repo.name}, Page: {page}");
                        break;
                    }
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<PullRequest>>(content);

                    Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Count: {result?.Count}");
                    pulls.AddRange(result);

                    if (result?.Count == 0 || result.Any(x => x.merged_at < sinceDate)) break;
                    page++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Error: {ex.Message}");
        }

        return pulls;
    }

    private async Task<List<Commit>> GetCommits(User user, string org, Repo repo)
    {
        var pulls = new List<Commit>();
        var page = 1;

        try
        {
            while (true)
            {
                Console.WriteLine($"calling API for: {repo.name}, Page: {page}");
                string startDate = sinceDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string finalDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{org ?? user.login}/{repo.name}/commits?author=ferbdev&since={sinceDate}&until={finalDate}&per_page=100&page={page}"))
                {
                    var response = await client.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Fail to get pull requests from: {repo.name}, Page: {page}");
                        break;
                    }
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<List<Commit>>(content);

                    Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Count: {result?.Count}");
                    pulls.AddRange(result);

                    if (result?.Count == 0) break;
                    page++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Error: {ex.Message}");
        }

        return pulls;
    }

    private async Task<CommitDetails?> GetCommitDetails(User user, string org, Repo repo, string commitSha)
    {
        var page = 1;

        try
        {
            Console.WriteLine($"calling API for: {repo.name}, Page: {page}");
            string startDate = sinceDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string finalDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{org ?? user.login}/{repo.name}/commits/{commitSha}"))
            {
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Fail to get commit detail from: {repo.name}, Page: {page}");
                    return null;
                }
                var content = await response.Content.ReadAsStringAsync();
                var commitDetail = JsonSerializer.Deserialize<CommitDetails>(content);

                return commitDetail;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Error: {ex.Message}");

            throw;
        }
    }
    
    private async Task<ExpandoObject?> GetLanguages(User user, string org, Repo repo)
    {
        var page = 1;

        try
        {
            Console.WriteLine($"calling API for: {repo.name}, Page: {page}");
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{org ?? user.login}/{repo.name}/languages"))
            {
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Fail to get commit detail from: {repo.name}, Page: {page}");
                    return null;
                }
                var content = await response.Content.ReadAsStringAsync();
                var commitDetail = JsonSerializer.Deserialize<ExpandoObject>(content);

                return commitDetail;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Result API for: {repo.name}, Page: {page}, Error: {ex.Message}");

            throw;
        }
    }

    public class User
    {
        public string login { get; set; }
        public string name { get; set; }
        public string company { get; set; }
        public string avatar_url { get; set; }
    }

    public class Repo
    {
        public string name { get; set; }
    }

    public class PullRequest
    {
        public DateTime? merged_at { get; set; }
    }

    public class Commit
    {
        public string sha { get; set; }
    }

    public class CommitDetails
    {
        public string repo { get; set; }
        public Stats stats { get; set; }
        public Commit commit { get; set; }

        public class Stats
        {
            public int total { get; set; }
        }

        public class Commit
        {
            public string message { get; set; }
            public Author author { get; set; }
            public class Author
            {
                public string date { get; set; }
            }
        }
    }
}
