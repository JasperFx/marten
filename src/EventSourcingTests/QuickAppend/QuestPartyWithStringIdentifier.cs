using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;

namespace EventSourcingTests.QuickAppend;

public class QuestPartyWithStringIdentifier
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

    public void Apply(MembersJoined joined)
    {
        if (joined.Members != null)
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

    public string Id { get; set; }

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}