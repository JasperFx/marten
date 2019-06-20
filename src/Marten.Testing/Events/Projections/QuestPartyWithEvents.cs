using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;

namespace Marten.Testing.Events.Projections
{
    // SAMPLE: QuestPartyWithEvents
    public class QuestPartyWithEvents
    {
        private readonly IList<string> _members = new List<string>();

        public string[] Members
        {
            get
            {
                return _members.ToArray();
            }
            set
            {
                _members.Clear();
                _members.AddRange(value);
            }
        }

        public IList<string> Slayed { get; } = new List<string>();

        public void Apply(Event<MembersJoined> joined)
        {
            _members.Fill(joined.Data.Members);
        }

        public void Apply(Event<MembersDeparted> departed)
        {
            _members.RemoveAll(x => departed.Data.Members.Contains(x));
        }

        public void Apply(Event<QuestStarted> started)
        {
            Name = started.Data.Name;
        }

        public string Name { get; set; }

        public Guid Id { get; set; }

        public override string ToString()
        {
            return $"Quest party '{Name}' is {Members.Join(", ")}";
        }
    }

    // ENDSAMPLE
}
