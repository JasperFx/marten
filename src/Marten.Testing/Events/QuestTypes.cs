using System;
using System.Linq;
using Baseline;
using Marten.Events;

namespace Marten.Testing.Events
{
    public class Quest : IAggregate
    {
        public Guid Id { get; set; }
    }


    public class ArrivedAtLocation : IEvent
    {
        public Guid Id { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public override string ToString()
        {
            return $"Arrived at {Location} on Day {Day}";
        }
    }

    public class MembersJoined : IEvent
    {
        public Guid Id { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }

        public override string ToString()
        {
            return $"Members {Members.Join(", ")} joined at {Location} on Day {Day}";
        }
    }


    public class QuestStarted : IEvent
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest {Name} started";
        }
    }

    public class MembersDeparted : IEvent
    {
        public Guid Id { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }

        public override string ToString()
        {
            return $"Members {Members.Join(", ")} departed at {Location} on Day {Day}";
        }
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