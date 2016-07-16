using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Testing.CodeTracker
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
            get { return _contributors.OrderBy(x => x).ToArray(); }
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

        protected bool Equals(ActiveProject other)
        {
            return string.Equals(ProjectName, other.ProjectName) && string.Equals(OrganizationName, other.OrganizationName) && LinesOfCode == other.LinesOfCode && OpenIssueCount == other.OpenIssueCount && Contributors.SequenceEqual(other.Contributors);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActiveProject) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (ProjectName != null ? ProjectName.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (OrganizationName != null ? OrganizationName.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ LinesOfCode;
                hashCode = (hashCode*397) ^ OpenIssueCount;
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"ProjectName: {ProjectName}, OrganizationName: {OrganizationName}, LinesOfCode: {LinesOfCode}, OpenIssueCount: {OpenIssueCount}, Contributors: {Contributors}";
        }
    }

    
}