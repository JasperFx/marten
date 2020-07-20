using System;
using System.Collections.Generic;

namespace Marten.Testing.Events.Projections
{
    public class Monster
    {
        public Guid Id { get; set; }
    }

    public class MonsterSlayed
    {
        public Guid QuestId { get; set; }
        public string Name { get; set; }

        protected bool Equals(MonsterSlayed other)
        {
            return QuestId.Equals(other.QuestId) && Name == other.Name;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MonsterSlayed) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(QuestId, Name);
        }
    }

    public class MonsterDestroyed
    {
        public Guid QuestId { get; set; }
        public string Name { get; set; }
    }

    public class MonsterQuestsAdded
    {
        public List<Guid> QuestIds { get; set; }
        public string Name { get; set; }
    }

    public class MonsterQuestsRemoved
    {
        public List<Guid> QuestIds { get; set; }
        public string Name { get; set; }
    }
}
