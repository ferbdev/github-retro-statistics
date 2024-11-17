using Application.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Service.Interface;

public interface IGithubStatisticsService
{
    event Action<List<RankingItem>> GithubStatisticsUpdated;
    Task GetStatistics(string organization, string ghToken);
}
