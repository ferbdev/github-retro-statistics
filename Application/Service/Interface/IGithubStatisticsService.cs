using Application.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Application.Service.GithubStatisticsService;

namespace Application.Service.Interface;

public interface IGithubStatisticsService
{
    event Action<List<RankingItem>> GithubStatisticsUpdated;
    Task GetStatistics(string organization, string ghToken);
    Task<User> GetUser(string ghToken);
}
