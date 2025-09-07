using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;

namespace ContainerScopedProjectionTests;

public class LetterProjection: IProjection
{
    private readonly IPriceLookup _lookup;

    public LetterProjection(IPriceLookup lookup)
    {
        _lookup = lookup;
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var counts = new LetterCounts
        {
            ACount = events.Where(x => x.EventType == typeof(AEvent)).Count(),
            BCount = events.Where(x => x.EventType == typeof(BEvent)).Count(),
            CCount = events.Where(x => x.EventType == typeof(CEvent)).Count(),
            DCount = events.Where(x => x.EventType == typeof(DEvent)).Count(),
            Price = _lookup.PriceFor(Guid.NewGuid().ToString())
        };

        operations.Store(counts);

        return Task.CompletedTask;
    }
}

[ProjectionVersion(3)]
public class LetterProjectionV3: IProjection
{
    private readonly IPriceLookup _lookup;

    public LetterProjectionV3(IPriceLookup lookup)
    {
        _lookup = lookup;
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var counts = new LetterCounts
        {
            ACount = events.Where(x => x.EventType == typeof(AEvent)).Count(),
            BCount = events.Where(x => x.EventType == typeof(BEvent)).Count(),
            CCount = events.Where(x => x.EventType == typeof(CEvent)).Count(),
            DCount = events.Where(x => x.EventType == typeof(DEvent)).Count(),
            Price = _lookup.PriceFor(Guid.NewGuid().ToString())
        };

        operations.Store(counts);

        return Task.CompletedTask;
    }
}
