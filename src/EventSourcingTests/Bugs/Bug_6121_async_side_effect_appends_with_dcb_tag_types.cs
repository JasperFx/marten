using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
///     Backported from marten#6121 (master). In a store that registers a DCB tag type, a side effect that appended
///     events from an async projection failed every batch with a <see cref="NullReferenceException" />:
///     <c>ProjectionBatch.QuickAppendEvents</c> never set the operation's <c>EventGraph</c>, which the quick append
///     reads to write each tag type's column.
/// </summary>
public class Bug_6121_async_side_effect_appends_with_dcb_tag_types: OneOffConfigurationsContext
{
    public readonly record struct Bug6121AccountTag(Guid Value);

    public record Bug6121LedgerOpened(string Number);

    public record Bug6121LedgerPosted;

    public record Bug6121AccountPosted(Guid Account);

    public class Bug6121Ledger
    {
        public Guid Id { get; set; }
        public int Postings { get; set; }
    }

    /// <summary>Raises an event from its side effects.</summary>
    public class AuditingLedgerProjection: SingleStreamProjection<Bug6121Ledger, Guid>
    {
        public AuditingLedgerProjection()
        {
            Name = "AuditingLedger";
        }

        public void Apply(Bug6121LedgerOpened e, Bug6121Ledger ledger)
        {
        }

        public void Apply(Bug6121LedgerPosted e, Bug6121Ledger ledger) => ledger.Postings++;

        public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<Bug6121Ledger> slice)
        {
            if (slice.Snapshot != null)
            {
                slice.AppendEvent(Guid.NewGuid(), new Bug6121AccountPosted(slice.Snapshot.Id));
            }

            return new ValueTask();
        }
    }

    [Fact]
    public async Task a_side_effect_can_append_events_in_a_store_with_dcb_tag_types()
    {
        StoreOptions(opts =>
        {
            opts.Events.RegisterTagType<Bug6121AccountTag>("account");
            opts.Projections.Add(new AuditingLedgerProjection(), ProjectionLifecycle.Async);
        });

        Guid stream;
        await using (var session = theStore.LightweightSession())
        {
            stream = session.Events.StartStream<Bug6121Ledger>(new Bug6121LedgerOpened("A"), new Bug6121LedgerPosted()).Id;
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(60.Seconds());

        await using var query = theStore.QuerySession();
        var raised = await query.Events.QueryRawEventDataOnly<Bug6121AccountPosted>()
            .ToListAsync(TestContext.Current.CancellationToken);
        raised.ShouldHaveSingleItem().ShouldBe(new Bug6121AccountPosted(stream));

        await daemon.StopAllAsync();
    }
}
