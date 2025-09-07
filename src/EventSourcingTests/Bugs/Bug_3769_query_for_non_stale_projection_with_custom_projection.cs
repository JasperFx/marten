using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3769_query_for_non_stale_projection_with_custom_projection : BugIntegrationContext
{
    // TODO -- going to require a patch to JasperFx for this
    [Fact]
    public async Task can_use_custom_aggregation_with_non_stale_query_data()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new CustomAggregateProjection(), ProjectionLifecycle.Async, asyncConfiguration:x => x.StorageTypes.Add(typeof(SimpleAggregate)));
        });

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new AEvent(), new BEvent(), new CEvent());
        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new AEvent(), new CEvent(), new CEvent());
        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var results = await theSession.QueryForNonStaleData<SimpleAggregate>(5.Seconds())
            .Where(x => x.CCount == 2).ToListAsync();

        results.Count.ShouldBe(2);
    }
}

public class CustomAggregateProjection: IProjection
{
    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var groups = events.GroupBy(x => x.StreamId);
        foreach (var group in groups)
        {
            var aggregate = await operations.LoadAsync<SimpleAggregate>(group.Key, cancellation);
            aggregate ??= new SimpleAggregate { Id = group.Key };

            foreach (var e in group)
            {
                switch (e.Data)
                {
                    case AEvent:
                        aggregate.ACount++;
                        break;
                    case BEvent:
                        aggregate.BCount++;
                        break;
                    case CEvent:
                        aggregate.CCount++;
                        break;
                }
            }

            operations.Store(aggregate);
        }
    }

}
