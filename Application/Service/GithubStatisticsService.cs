using Application.Model;
using Application.Service.Interface;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Application.Service;

public class GithubStatisticsService : IGithubStatisticsService
{
    public event Action<List<RankingItem>> GithubStatisticsUpdated;
    private List<RankingItem> rankingItems = new List<RankingItem>();
    private DateTime sinceDate = DateTime.UtcNow.AddDays(-30);

    private readonly HttpClient client = new HttpClient();

    public async Task GetStatistics(string organization, string ghToken)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", ghToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        client.Timeout = new TimeSpan(0, 0, 5);

        var repos = await GetRepos(organization);
        var mergeCount = new Dictionary<string, int>();

        Console.WriteLine($"Total repos: {repos.Count}");

        var maxDegreeOfParallelism = 100; // Ajuste conforme necessário
        var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

        var tasks = repos.Select(async repo =>
        {
            await semaphore.WaitAsync();
            try
            {
                var pulls = await GetPullRequests(organization, repo);
                Console.WriteLine($"Repo: {repo.name} {pulls.Count} PRs");

                foreach (var pull in pulls)
                {
                    if (pull.merged_at.HasValue && pull.merged_at.Value > sinceDate)
                    {
                        if (!mergeCount.ContainsKey(repo.name))
                        {
                            mergeCount[repo.name] = 0;
                        }
                        mergeCount[repo.name]++;
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var topRepos = mergeCount.OrderByDescending(kv => kv.Value).Take(10);
        rankingItems = mergeCount.OrderByDescending(kv => kv.Value).Take(10).Select((x, index) => new RankingItem { Position = index + 1, Name = $"{x.Key} - {x.Value} PRs" }).ToList();
        Console.WriteLine("Top 10 repositórios com mais pull requests merged nos últimos 30 dias:");
        foreach (var (repoName, count, index) in topRepos.Select((kv, index) => (kv.Key, kv.Value, index)))
        {
            Console.WriteLine($"{repoName}: {count} pull requests merged");
        }

        GithubStatisticsUpdated?.Invoke(rankingItems);
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

    public class Repo
    {
        public string name { get; set; }
    }

    public class PullRequest
    {
        public DateTime? merged_at { get; set; }
    }
}
