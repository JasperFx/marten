using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public record Bug4778Event(string Name);

public class Bug4778Projection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// #4778: mixing OverwriteEventOperation (a plain UPDATE with no RETURNING) with a RETURNING
/// upsert (an optimistic-concurrency document) in a single SaveChanges batch throws a false
/// ConcurrencyException. OverwriteEventOperation is not marked NoDataReturnedCall, so the
/// OperationPage walk fires a spurious NextResultAsync() that consumes the upsert's result-set,
/// leaving the upsert's reader exhausted.
/// </summary>
public class Bug_4778_overwrite_event_with_returning_upsert: OneOffConfigurationsContext
{
    [Fact]
    public async Task overwrite_events_then_occ_upsert_in_one_batch_does_not_throw()
    {
        StoreOptions(opts => opts.Schema.For<Bug4778Projection>().UseOptimisticConcurrency(true));

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Bug4778Event("a"), new Bug4778Event("b"));
            await session.SaveChangesAsync();
        }

        IReadOnlyList<IEvent> events;
        await using (var query = theStore.QuerySession())
        {
            events = await query.Events.FetchStreamAsync(streamId);
        }

        await using (var session = theStore.LightweightSession())
        {
            // Mixed batch: non-RETURNING OverwriteEvent UPDATEs queued first, then a RETURNING OCC upsert.
            foreach (var e in events)
            {
                session.Events.OverwriteEvent(e);
            }

            session.Store(new Bug4778Projection { Id = streamId, Name = "projected" });

            // Today this throws a false ConcurrencyException.
            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }

        await using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Bug4778Projection>(streamId)).ShouldNotBeNull();
        }
    }
}
