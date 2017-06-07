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