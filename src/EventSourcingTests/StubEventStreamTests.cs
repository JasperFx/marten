using EventSourcingTests.Projections;
using Marten.Events;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class StubEventStreamTests
{
    [Fact]
    public void append_one()
    {
        var stream = new StubEventStream<QuestParty>(new QuestParty());
        stream.AppendOne(new QuestStarted());

        stream.EventsAppended.Count.ShouldBe(1);
    }

    [Fact]
    public void append_many()
    {
        var stream = new StubEventStream<QuestParty>(new QuestParty());
        stream.AppendMany(new QuestStarted(), new MonsterDefeated());
        stream.EventsAppended.Count.ShouldBe(2);
    }
}
