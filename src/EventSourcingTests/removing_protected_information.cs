using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class removing_protected_information
{
    private readonly EventGraph theEvents = new EventGraph(new StoreOptions());

    [Fact]
    public void miss_on_masking()
    {
        theEvents.TryMask(new Event<AEvent>(new AEvent()))
            .ShouldBeFalse();
    }

    [Fact]
    public void match_exactly_on_event_type()
    {
        theEvents.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "****");

        var started = new QuestStarted { Name = "Find the Eye of the World" };

        var @event = new Event<QuestStarted>(started);

        theEvents.TryMask(@event).ShouldBeTrue();

        started.Name.ShouldBe("****");

    }

    [Fact]
    public void apply_with_contra_variance()
    {
        theEvents.AddMaskingRuleForProtectedInformation<IAccountEvent>(x => x.Name = "****");

        var changed = new AccountChanged { Name = "Harry" };

        var @event = new Event<AccountChanged>(changed);

        theEvents.TryMask(@event).ShouldBeTrue();

        changed.Name.ShouldBe("****");
    }
}

public interface IAccountEvent
{
    string Name { get; set; }
}

public class AccountChanged: IAccountEvent
{
    public string Name { get; set; }
}


