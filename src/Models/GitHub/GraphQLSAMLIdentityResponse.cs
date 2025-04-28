using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models.GitHub
{

    public class GraphQLSAMLIdentityResponse
    {
        public Organization organization { get; set; }

        public class Organization
        {
            public Samlidentityprovider samlIdentityProvider { get; set; }

            public Organization()
            {
                samlIdentityProvider = new Samlidentityprovider();
            }
        }

        public class Samlidentityprovider
        {
            public string? id { get; set; }
            public string? ssoUrl { get; set; }
            public string? issuer { get; set; }

            public Externalidentities? externalIdentities { get; set; }
        }

        public class Externalidentities
        {
            public int totalCount { get; set; }
            public Pageinfo? pageInfo { get; set; }
            public List<Edge>? edges { get; set; }
        }

        public class Pageinfo
        {
            public bool hasNextPage { get; set; }
            public string? endCursor { get; set; }
        }

        public class Edge
        {
            public Node? node { get; set; }
        }

        public class Node
        {
            public User? user { get; set; }
            public Samlidentity? samlIdentity { get; set; }
        }

        public class User
        {
            public string? id { get; set; }
            public string? login { get; set; }
            public string? name { get; set; }
            public string? email { get; set; }
        }

        public class Samlidentity
        {
            public string? nameId { get; set; }
        }
    }




}