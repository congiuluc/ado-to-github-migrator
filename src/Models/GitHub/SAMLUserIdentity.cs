﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models.GitHub
{
    public class SAMLUserIdentity
    {
        public string Id { get; set; } = "";
        public string Login { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string SamlIdentity { get; set; } = "";


    }



}
