using System;
using System.Linq;
using Baseline;

namespace Marten.Testing.Events
{
    public class Quest
    {
        public Guid Id { get; set; }
    }

    #region sample_sample-events
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

        protected bool Equals(MembersJoined other)
        {
            return QuestId.Equals(other.QuestId) && Day == other.Day && Location == other.Location && Members.SequenceEqual(other.Members);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MembersJoined) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(QuestId, Day, Location, Members);
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

        protected bool Equals(QuestStarted other)
        {
            return Name == other.Name && Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((QuestStarted) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Id);
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

    #endregion sample_sample-events

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
