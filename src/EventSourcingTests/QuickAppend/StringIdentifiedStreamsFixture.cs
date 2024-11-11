using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;

namespace EventSourcingTests.QuickAppend;

public class StringIdentifiedStreamsFixture: StoreFixture
{
    public StringIdentifiedStreamsFixture(): base("quick_string_identified_streams")
    {
        Options.Events.AppendMode = EventAppendMode.Quick;
        Options.Events.StreamIdentity = StreamIdentity.AsString;
        Options.Projections.Snapshot<QuestPartyWithStringIdentifier>(ProjectionLifecycle.Inline);

        Options.Events.AddEventType(typeof(MembersJoined));
        Options.Events.AddEventType(typeof(MembersDeparted));
        Options.Events.AddEventType(typeof(QuestStarted));
    }
}
