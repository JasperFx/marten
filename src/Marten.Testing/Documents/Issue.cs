using System;

namespace Marten.Testing.Documents
{
    public class Issue
    {
        public Issue()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string Title { get; set; }
    }
}