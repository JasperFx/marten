using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Events.Aggregation;

namespace EventSourcingTests.Projections;


public class QuestParty
{
    public List<string> Members { get; set; } = new();
    public IList<string> Slayed { get; } = new List<string>();
    public string Key { get; set; }
    public string Name { get; set; }

    // In this particular case, this is also the stream id for the quest events
    public Guid Id { get; set; }


    // These methods take in events and update the QuestParty
    public void Apply(MembersJoined joined) => Members.Fill(joined.Members);
    public void Apply(MembersDeparted departed) => Members.RemoveAll(x => departed.Members.Contains(x));
    public void Apply(QuestStarted started) => Name = started.Name;

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}


public class QuestFinishingParty: QuestParty
{
    private readonly string _exMachina;

    public QuestFinishingParty()
    {
    }

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

        Members.RemoveAll(x => escaped.Members.Contains(x));
    }
}
