using System;
using Baseline;
using Marten.Events;

namespace Marten.Testing.Events
{
    public class Quest
    {
        public Guid Id { get; set; }
    }

    // SAMPLE: sample-events
    public class ArrivedAtLocation
    {

        public int Day { get; set; }

        public string Location { get; set; }

        public override string ToString()
        {
            return $"Arrived at {Location} on Day {Day}";
        }
    }

    public class MembersJoined
    {

        public MembersJoined()
        {
        }

        public MembersJoined(int day, string location, params string[] members)
        {
            Day = day;
            Location = location;
            Members = members;
        }

        public Guid QuestId { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }

        public override string ToString()
        {
            return $"Members {Members.Join(", ")} joined at {Location} on Day {Day}";
        }
    }


    public class QuestStarted
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest {Name} started";
        }
    }

    public class QuestEnded
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest {Name} ended";
        }
    }

    public class MembersDeparted
    {
        public Guid Id { get; set; }

        public Guid QuestId { get; set; }

        public int Day { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }

        public override string ToString()
        {
            return $"Members {Members.Join(", ")} departed at {Location} on Day {Day}";
        }
    }

    public class MembersEscaped
    {
        public Guid Id { get; set; }

        public Guid QuestId { get; set; }

        public string Location { get; set; }

        public string[] Members { get; set; }

        public override string ToString()
        {
            return $"Members {Members.Join(", ")} escaped from {Location}";
        }
    }
    // ENDSAMPLE

    public class Issue
    {
        public Guid Id { get; set; }
    }

    public class IssueCreated
    {
        public Guid Id { get; set; }
    }

    public class IssueAssigned
    {
        public Guid Id { get; set; }
    }

    public class ImmutableEvent
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }

        public ImmutableEvent(Guid id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class ImmutableEvent2
    {
        public string Key { get; }
        public string Name { get; private set; }


        public ImmutableEvent2(string key, string name)
        {
            Key = key;
            Name = name;
        }
    }
}