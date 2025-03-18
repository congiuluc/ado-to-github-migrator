using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models.GitHub
{
    public class GitHubUser
    {

        public string? Id { get; set; }
        public string? Login { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }

        public string? OrgRole { get; set; }
    }
}
