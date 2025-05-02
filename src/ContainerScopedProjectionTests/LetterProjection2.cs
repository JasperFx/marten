using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;

namespace ContainerScopedProjectionTests;

public class LetterProjection2: EventProjection
{
    private readonly IPriceLookup _lookup;

    public LetterProjection2(IPriceLookup lookup)
    {
        _lookup = lookup;
        Name = "Letters";
        Version = 2;
        IncludeType<AEvent>();
        IncludeType<BEvent>();
        IncludeType<CEvent>();
        IncludeType<DEvent>();

        FilterIncomingEventsOnStreamType(typeof(LetterCounts));

        StreamType = typeof(LetterCounts);

        Options.BatchSize = 111;

        IncludeArchivedEvents = true;
    }

    public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        if (e.Data is AEvent)
        {
            operations.Store(new LetterCounts{ACount = 1, Price = _lookup.PriceFor(e.Id.ToString())});
        }

        return new ValueTask();
    }
}

[ProjectionVersion(3)]
public class LetterProjection2V3: EventProjection
{
    private readonly IPriceLookup _lookup;

    public LetterProjection2V3(IPriceLookup lookup)
    {
        _lookup = lookup;
        Name = "Letters";
        IncludeType<AEvent>();
        IncludeType<BEvent>();
        IncludeType<CEvent>();
        IncludeType<DEvent>();

        FilterIncomingEventsOnStreamType(typeof(LetterCounts));

        StreamType = typeof(LetterCounts);

        Options.BatchSize = 111;

        IncludeArchivedEvents = true;
    }

    public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        if (e.Data is AEvent)
        {
            operations.Store(new LetterCounts{ACount = 1, Price = _lookup.PriceFor(e.Id.ToString())});
        }

        return new ValueTask();
    }
}
