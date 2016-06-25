using System;

namespace CodeTracker
{
    public abstract class Timestamped
    {
        public DateTimeOffset Timestamp { get; set; }
    }


    public class ProjectStarted : Timestamped
    {
        public string Name { get; set; }
        public string Organization { get; set; }
    }

    public class IssueCreated : Timestamped
    {
        public int Number { get; set; }

        public string UserName { get; set; }

        public string Description { get; set; }


    }

    public class IssueClosed : Timestamped
    {
        public int Number { get; set; }

        public string UserName { get; set; }

    }

    public class IssueReopened : Timestamped
    {
        public int Number { get; set; }

        public string UserName { get; set; }

    }


    public class Commit : Timestamped
    {
        public string UserName { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public string Sha { get; set; }
        public string Message { get; set; }
    }
}