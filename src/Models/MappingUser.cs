using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOps2GitHubMigrator.Models
{
    /// <summary>
    /// Represents a user mapping between Azure DevOps and GitHub users
    /// </summary>
    public class MappingUser
    {
        /// <summary>
        /// Gets or sets the Azure DevOps username
        /// </summary>
        public string? AdoUser { get; set; }

        /// <summary>
        /// Gets or sets the display name of the user
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the email address of the user
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Gets or sets the GitHub username
        /// </summary>
        public string? GitHubUser { get; set; }  
    }
}
