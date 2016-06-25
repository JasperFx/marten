using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace CodeTracker
{
    public class ActiveProject
    {
        public ActiveProject()
        {
        }

        public ActiveProject(string organizationName, string projectName)
        {
            ProjectName = projectName;
            OrganizationName = organizationName;
        }

        public Guid Id { get; set; }
        public string ProjectName { get; set; }

        public string OrganizationName { get; set; }

        public int LinesOfCode { get; set; }

        public int OpenIssueCount { get; set; }

        private readonly IList<string> _contributors = new List<string>();

        public string[] Contributors
        {
            get { return _contributors.ToArray(); }
            set
            {
                _contributors.Clear();
                _contributors.AddRange(value);
            }
        }

        public void Apply(ProjectStarted started)
        {
            ProjectName = started.Name;
            OrganizationName = started.Organization;
        }

        public void Apply(IssueCreated created)
        {
            OpenIssueCount++;
        }

        public void Apply(IssueReopened reopened)
        {
            OpenIssueCount++;
        }

        public void Apply(IssueClosed closed)
        {
            OpenIssueCount--;
        }

        public void Apply(Commit commit)
        {
            _contributors.Fill(commit.UserName);
            LinesOfCode += (commit.Additions - commit.Deletions);
        }

    }

    
}