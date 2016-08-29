using System;

namespace Marten.Testing.Events.Projections
{
    public class MonsterSlayed
    {
        public Guid QuestId { get; set; }
        public string Name { get; set; }
    }
}