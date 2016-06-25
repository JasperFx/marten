using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace CodeTracker
{
    public class UserAction
    {
        public UserAction()
        {
        }

        public UserAction(string userName, DateTimeOffset timestamp)
        {
            UserName = userName;
            Timestamp = timestamp;
        }

        public string UserName { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class IssueState
    {
        public Guid ProjectId { get; set; }
        public int Number { get; set; }
        public string Id { get; set; }

        public bool IsOpen { get; set; }

        public UserAction Created { get; set; }
        public UserAction Closed { get; set; }

        public IssueState()
        {
        }

        public IssueState(Guid projectId, int number)
        {
            ProjectId = projectId;
            Number = number;
        }
    }

    public class CommitView
    {
        [Identity]
        public string Sha { get; set; }

        public string Title { get; set; }

        public Guid ProjectId { get; set; }

        public int Additions { get; set; }

        public int Deletions { get; set; }

        public DateTimeOffset Timestamp { get; set; }

    }

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

    }
}