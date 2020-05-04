using System;

namespace Marten.Schema.Testing.Documents
{
    public class CriticalIssue: Issue
    {
        public DateTime BecameCritical { get; set; }
    }
}
