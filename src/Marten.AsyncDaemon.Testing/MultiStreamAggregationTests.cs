using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.AsyncDaemon.Testing;

public class MultiStreamAggregationTests: OneOffConfigurationsContext
{
    [Fact]
    public async Task events_applied_in_sequence_across_streams()
    {
        StoreOptions(opts => opts.Projections.Add<Projector>(ProjectionLifecycle.Inline));

        var commonId = Guid.NewGuid();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await using var session = theStore.LightweightSession();

        session.Events.StartStream(commonId, new Happened() { Id = commonId }, new Happened() { Id = commonId });
        await session.SaveChangesAsync();

        session.Events.StartStream(new Happened() { Id = commonId }, new Happened() { Id = commonId });
        await session.SaveChangesAsync();

        var projection = await session.LoadAsync<Projection>(commonId);
        var eventSequenceList = new List<long> { 1, 2, 3, 4 };
        projection.ShouldNotBeNull();
        projection.EventSequenceList.ShouldHaveTheSameElementsAs(eventSequenceList);

        await daemon.RebuildProjectionAsync<Projector>(CancellationToken.None);
        projection = await session.LoadAsync<Projection>(commonId);
        projection.ShouldNotBeNull();
        projection.EventSequenceList.ShouldHaveTheSameElementsAs(eventSequenceList);
    }

    public interface ICommonId
    {
        public Guid Id { get; set; }
    }

    public class Happened: ICommonId
    {
        public Guid Id { get; set; }
    }

    public class Projection
    {
        public Guid Id { get; set; }
        public IList<long> EventSequenceList { get; set; } = new List<long>();
    }

    public class Projector: MultiStreamProjection<Projection, Guid>
    {
        public Projector()
        {
            Identity<ICommonId>(x => x.Id);
        }

        public void Apply(Projection p, IEvent<Happened> e) => p.EventSequenceList.Add(e.Sequence);
    }
}
