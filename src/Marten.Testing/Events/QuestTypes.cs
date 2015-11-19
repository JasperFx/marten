using System;
using Marten.Events;

namespace Marten.Testing.Events
{
    public class Quest : IAggregate
    {
        public Guid Id { get; set; }
    }


    public class QuestStarted : IEvent
    {
        public Guid Id { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }
    }

    public class MembersJoined : IEvent
    {
        public Guid Id { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }
    }

    public class MembersDeparted : IEvent
    {
        public Guid Id { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }
    }

    public class Issue : IAggregate
    {
        public Guid Id { get; set; }
    }

    public class IssueCreated : IEvent
    {
        public Guid Id { get; set; }
    }

    public class IssueAssigned : IEvent
    {
        public Guid Id { get; set; }
    }
}