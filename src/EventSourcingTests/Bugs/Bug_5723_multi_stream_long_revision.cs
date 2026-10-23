using System;
using System.Threading.Tasks;
using Marten.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5723. A multi-stream projection's revision is the event's global sequence, and the advice from #4526 is to
/// put such a document on <see cref="ILongVersioned" /> so a sequence past <see cref="int.MaxValue" /> fits.
/// The bigint column did, but <c>StoreProjection</c> cast the sequence through <c>int</c> before it got there,
/// so the revision wrapped negative.
/// </summary>
public class Bug_5723_multi_stream_long_revision: OneOffConfigurationsContext
{
    private const long PastInt32 = (long)int.MaxValue + 100;

    [Fact]
    public async Task a_multi_stream_revision_past_int32_is_not_truncated()
    {
        StoreOptions(opts => opts.Projections.Add<LedgerTotalProjection>(ProjectionLifecycle.Async));

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(JasperFx.Events.IEvent));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        // Move the global sequence past int.MaxValue rather than appending two billion events
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"alter sequence {SchemaName}.mt_events_sequence restart with {PastInt32}")
                .ExecuteNonQueryAsync();
        }

        var ledger = Guid.NewGuid();
        theSession.Events.StartStream(Guid.NewGuid(), new LedgerEntryPosted(ledger, 10));
        await theSession.SaveChangesAsync();

        // StoreProjection is the daemon's path; an inline projection stores through Store(), which does not take
        // its revision from the event at all
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(TimeSpan.FromSeconds(30));
        }

        await using var query = theStore.QuerySession();
        var total = await query.LoadAsync<LedgerTotal>(ledger);

        total.ShouldNotBeNull();
        total.Amount.ShouldBe(10);
        total.Version.ShouldBe(PastInt32);
    }
}

public record LedgerEntryPosted(Guid LedgerId, decimal Amount);

public class LedgerTotal: ILongVersioned
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public long Version { get; set; }
}

public partial class LedgerTotalProjection: MultiStreamProjection<LedgerTotal, Guid>
{
    public LedgerTotalProjection()
    {
        Identity<LedgerEntryPosted>(x => x.LedgerId);
    }

    public void Apply(LedgerTotal total, LedgerEntryPosted e) => total.Amount += e.Amount;
}
