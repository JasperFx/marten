using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Testing.Events.Projections
{
    public class QuestParty
    {
        public readonly IList<string> Members = new List<string>();
        public readonly IList<string> Slayed = new List<string>();

        public void Apply(MembersJoined joined)
        {
            Members.Fill(joined.Members);
        }

        public void Apply(MembersDeparted departed)
        {
            Members.RemoveAll(x => departed.Members.Contains(x));
        }

        public void Apply(QuestStarted started)
        {
            Name = started.Name;
        }

        public string Name { get; set; }
        public Guid Id { get; set; }
    }
}