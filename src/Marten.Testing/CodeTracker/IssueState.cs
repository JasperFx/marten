using System;

namespace Marten.Testing.CodeTracker
{
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


}