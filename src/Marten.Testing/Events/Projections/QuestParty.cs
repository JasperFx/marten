using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;

namespace Marten.Testing.Events.Projections
{
    // SAMPLE: QuestParty
    public class QuestParty
    {
        protected readonly IList<string> _members = new List<string>();

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


    public void Apply(MembersJoined joined)
    {
        _members.Fill(joined.Members);
    }

    public void Apply(MembersDeparted departed)
    {
        _members.RemoveAll(x => departed.Members.Contains(x));
    }

    public void Apply(QuestStarted started)
    {
        Name = started.Name;
    }


    public string Key { get; set; }

    public string Name { get; set; }

    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}

    public class QuestFinishingParty : QuestParty
    {
        private readonly string _exMachina;

        public QuestFinishingParty() { }

        public QuestFinishingParty(string exMachina)
        {
            _exMachina = exMachina;
        }
        
        public void Apply(MembersEscaped escaped)
        {
            if (_exMachina == null)
            {
                throw new NullReferenceException("Can't escape w/o an Ex Machina");
            }

            _members.RemoveAll(x => escaped.Members.Contains(x));
        }
    }
    // ENDSAMPLE
}