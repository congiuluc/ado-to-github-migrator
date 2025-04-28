using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models.GitHub
{

    public class GraphQLOrgUsersResponse
    {
        public Organization? organization { get; set; }
        public class Organization
        {
            public Memberswithrole? membersWithRole { get; set; }
        }

        public class Memberswithrole
        {
            public int totalCount { get; set; }
            public Pageinfo? pageInfo { get; set; }
            public Edge[]? edges { get; set; }
        }

        public class Pageinfo
        {
            public bool hasNextPage { get; set; }
            public string? endCursor { get; set; }
        }

        public class Edge
        {
            public Node? node { get; set; }
            public string? role { get; set; }
        }

        public class Node
        {
            public string? id { get; set; }
            public string? login { get; set; }
            public object? name { get; set; }
            public string? email { get; set; }
        }
    }

}
