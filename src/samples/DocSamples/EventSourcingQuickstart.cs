using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;

namespace DocSamples;

#region sample_sample-events

public sealed record ArrivedAtLocation(Guid QuestId, int Day, string Location);

public sealed record MembersJoined(Guid QuestId, int Day, string Location, string[] Members);

public sealed record QuestStarted(Guid QuestId, string Name);

public sealed record QuestEnded(Guid QuestId, string Name);

public sealed record MembersDeparted(Guid QuestId, int Day, string Location, string[] Members);

public sealed record MembersEscaped(Guid QuestId, string Location, string[] Members);


#endregion


#region sample_QuestParty

public sealed record QuestParty(Guid Id, List<string> Members)
{
    // These methods take in events and update the QuestParty
    public static QuestParty Create(QuestStarted started) => new(started.QuestId, []);
    public static QuestParty Apply(MembersJoined joined, QuestParty party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static QuestParty Apply(MembersDeparted departed, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static QuestParty Apply(MembersEscaped escaped, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };
}

#endregion

#region sample_Quest
public sealed record Quest(Guid Id, List<string> Members, List<string> Slayed, string Name, bool isFinished);

public sealed class QuestProjection: SingleStreamProjection<Quest>
{
    public static Quest Create(QuestStarted started) => new(started.QuestId, [], [], started.Name, false);
    public static Quest Apply(MembersJoined joined, Quest party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static Quest Apply(MembersDeparted departed, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static Quest Apply(MembersEscaped escaped, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };

    public static Quest Apply(QuestEnded ended, Quest party) =>
        party with { isFinished = true };

}

#endregion


public class EventSourcingQuickstart
{
    [Fact]
    public async Task capture_events()
    {
        #region sample_event-store-quickstart

        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
        });

        var questId = Guid.NewGuid();

        await using var session = store.LightweightSession();
        var started = new QuestStarted(questId, "Destroy the One Ring");
        var joined1 = new MembersJoined(questId,1, "Hobbiton", ["Frodo", "Sam"]);

        // Start a brand new stream and commit the new events as
        // part of a transaction
        session.Events.StartStream(questId, started, joined1);

        // Append more events to the same stream
        var joined2 = new MembersJoined(questId,3, "Buckland", ["Merry", "Pippen"]);
        var joined3 = new MembersJoined(questId,10, "Bree", ["Aragorn"]);
        var arrived = new ArrivedAtLocation(questId, 15, "Rivendell");
        session.Events.Append(questId, joined2, joined3, arrived);

        // Save the pending changes to db
        await session.SaveChangesAsync();

        #endregion

        #region sample_events-aggregate-on-the-fly

        await using var session2 = store.LightweightSession();
        // questId is the id of the stream
        var party = await session2.Events.AggregateStreamAsync<QuestParty>(questId);

        var party_at_version_3 = await session2.Events
            .AggregateStreamAsync<QuestParty>(questId, 3);

        var party_yesterday = await session2.Events
            .AggregateStreamAsync<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));

        #endregion

    }
    [Fact]
    public async Task quest_projection()
    {
        #region sample_adding-quest-projection
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Projections.Add<QuestProjection>(ProjectionLifecycle.Inline); // [!code ++]
        });
        #endregion

        var questId = Guid.NewGuid();

        #region sample_querying-quest-projection
        await using var session = store.LightweightSession();

        var started = new QuestStarted(questId, "Destroy the One Ring");
        var joined1 = new MembersJoined(questId, 1, "Hobbiton", ["Frodo", "Sam"]);

        session.Events.StartStream(questId, started, joined1);
        await session.SaveChangesAsync();

        // we can now query the quest state like any other Marten document
        var questState = await session.LoadAsync<Quest>(questId);

        var finishedQuests = await session.Query<Quest>().Where(x => x.isFinished).ToListAsync();

        #endregion

    }
}
