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
    event Action<TopLongCommit> TopLongCommitUpdated;
    Task GetTopLongCommit(User user, string organization, string ghToken);
    Task<User> GetUser(string ghToken);
}
