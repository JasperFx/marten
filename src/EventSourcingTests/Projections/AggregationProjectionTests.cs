using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Daemon;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class AggregationProjectionTests
{
    [Theory]
    [InlineData(typeof(AEvent), true)]
    [InlineData(typeof(BEvent), true)]
    [InlineData(typeof(CEvent), true)]
    [InlineData(typeof(DEvent), true)]
    [InlineData(typeof(EEvent), true)]
    [InlineData(typeof(UserDeleted), true)]
    [InlineData(typeof(Thing), true)]
    [InlineData(typeof(CreateEvent), true)]
    [InlineData(typeof(QuestStarted), false)]
    public void applies_to_type(Type eventType, bool shouldApply)
    {
        var aggregate = new SampleAggregate();
        aggregate.AppliesTo(new Type[]{eventType})
            .ShouldBe(shouldApply);
    }

    [Fact]
    public void adding_filter_for_aggregate_type()
    {
        var projection = new SampleAggregate();
        projection.AssembleAndAssertValidity();

        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        var filters = projection.BuildFilters(store);

        var filter = filters.OfType<AggregateTypeFilter>().Single();
        filter.AggregateType.ShouldBe(typeof(MyAggregate));
    }

    public class OtherAggregate
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public void adding_filter_for_another_aggregate_type()
    {
        var projection = new SingleStreamAggregation<MyAggregate>();
        projection.ProjectEvent<AEvent>(a => { });
        projection.FilterIncomingEventsOnStreamType(typeof(OtherAggregate));
        projection.AssembleAndAssertValidity();

        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        var filters = projection.BuildFilters(store);

        var filter = filters.OfType<AggregateTypeFilter>().Single();
        filter.AggregateType.ShouldBe(typeof(OtherAggregate));
    }
}

public interface IThing{}
public class Thing : IThing{}

public class SampleAggregate: SingleStreamAggregation<MyAggregate>
{
    public SampleAggregate()
    {
        DeleteEvent<UserDeleted>();

        FilterIncomingEventsOnStreamType();
    }

    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate
        {
            ACount = @event.A,
            BCount = @event.B,
            CCount = @event.C,
            DCount = @event.D
        };
    }

    public void Apply(IThing e, MyAggregate aggregate)
    {

    }

    public async Task Apply(UserUpdated @event, MyAggregate aggregate, IQuerySession session)
    {
        var user = await session.LoadAsync<User>(@event.UserId);
        aggregate.UpdatedBy = user.UserName;
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public void Apply(BEvent @event, MyAggregate aggregate)
    {
        aggregate.BCount++;
    }

    public void Apply(MyAggregate aggregate, CEvent @event)
    {
        aggregate.CCount++;
    }

    public void Apply(MyAggregate aggregate, DEvent @event)
    {
        aggregate.DCount++;
    }

    public bool ShouldDelete(MyAggregate aggregate, EEvent @event)
    {
        return true;
    }
}