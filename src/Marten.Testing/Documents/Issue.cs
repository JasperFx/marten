using System;

namespace Marten.Testing.Documents
{
    public class Issue
    {
        public Issue()
        {
            Id = Guid.NewGuid();
        }

        public string[] Tags { get; set; }

        public Guid Id { get; set; }

        public string Title { get; set; }

        public Guid? AssigneeId { get; set; }

        public Guid? ReporterId { get; set; }
    }
}