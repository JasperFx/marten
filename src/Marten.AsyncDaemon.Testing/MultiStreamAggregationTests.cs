#if NET6_0_OR_GREATER
using System;
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

        Assert.NotNull(projection);
        Assert.Equal("1234", projection.Events);

        await daemon.RebuildProjection<Projector>(CancellationToken.None);

        projection = await session.LoadAsync<Projection>(commonId);

        Assert.NotNull(projection);
        Assert.Equal("1234", projection.Events);
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
        public string Events { get; set; } = "";
    }

    public class Projector: MultiStreamAggregation<Projection, Guid>
    {
        public Projector()
        {
            Identity<ICommonId>(x => x.Id);
        }

        public void Apply(Projection p, IEvent<Happened> e) => p.Events += e.Sequence;
    }
}

#endif
