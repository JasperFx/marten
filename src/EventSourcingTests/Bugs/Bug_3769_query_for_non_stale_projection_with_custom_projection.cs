using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3769_query_for_non_stale_projection_with_custom_projection : BugIntegrationContext
{
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
    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        throw new System.NotImplementedException();
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        foreach (var stream in streams)
        {
            var aggregate = await operations.LoadAsync<SimpleAggregate>(stream.Id);
            aggregate ??= new SimpleAggregate { Id = stream.Id };

            foreach (var e in stream.Events)
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
