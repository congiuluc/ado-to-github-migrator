using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models.Ado
{

    public class TfvcBranch
    {
        public string? path { get; set; }
        public Owner? owner { get; set; }
        public DateTime createdDate { get; set; }
        public string? url { get; set; }
        public object[]? relatedBranches { get; set; }
        public object[]? mappings { get; set; }
        public Parent? parent { get; set; }
        public TfvcBranch[]? children { get; set; }
    }

    public class Owner
    {
        public string? displayName { get; set; }
        public string? url { get; set; }
        public string? id { get; set; }
        public string? uniqueName { get; set; }
        public string? imageUrl { get; set; }
    }

    public class Parent
    {
        public string? path { get; set; }
    }


}
