using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Model
{
    public class GithubConfigs
    {
        public string Token { get; set; }
        public string ClientId { get; set; } = "Ov23liMRT65Ce2BU5NGl";
        public string ClientSecret { get; set; } = "0ddcb5e4775f6cc2cbf443be1987b581ead7db61";
        public string Scope { get; set; } = "read:user repo read:org";
    }
}
