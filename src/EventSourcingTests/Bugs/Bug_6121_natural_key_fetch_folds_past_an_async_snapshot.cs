using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
///     Backported from marten#6121 (master). <c>FetchForWriting</c> and <c>FetchLatest</c> by natural key, on an
///     <c>Async</c> snapshot, returned the stored document without the events after it, but under the stream's
///     current version. They now fold forward, as the fetches by stream identity do.
/// </summary>
public class Bug_6121_natural_key_fetch_folds_past_an_async_snapshot: OneOffConfigurationsContext
{
    public record Bug6121NumberedLedgerOpened(string Number);

    public record Bug6121NumberedLedgerPosted;

    public record Bug6121LedgerNumber(string Value);

    public class Bug6121NumberedLedger
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public Bug6121LedgerNumber Number { get; set; } = null!;

        public int Postings { get; set; }

        [NaturalKeySource]
        public void Apply(Bug6121NumberedLedgerOpened e) => Number = new Bug6121LedgerNumber(e.Number);

        public void Apply(Bug6121NumberedLedgerPosted e) => Postings++;
    }

    public class Bug6121KeyedLedger
    {
        public string Id { get; set; } = string.Empty;

        [NaturalKey]
        public Bug6121LedgerNumber Number { get; set; } = null!;

        public int Postings { get; set; }

        [NaturalKeySource]
        public void Apply(Bug6121NumberedLedgerOpened e) => Number = new Bug6121LedgerNumber(e.Number);

        public void Apply(Bug6121NumberedLedgerPosted e) => Postings++;
    }

    [Fact]
    public async Task fetches_by_natural_key_fold_the_events_after_a_lagging_snapshot_with_guid_streams()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Bug6121NumberedLedger>(SnapshotLifecycle.Async));

        var token = TestContext.Current.CancellationToken;
        var number = new Bug6121LedgerNumber($"L-{Guid.NewGuid():N}");

        Guid stream;
        await using (var session = theStore.LightweightSession())
        {
            stream = session.Events.StartStream<Bug6121NumberedLedger>(new Bug6121NumberedLedgerOpened(number.Value),
                new Bug6121NumberedLedgerPosted()).Id;
            await session.SaveChangesAsync(token);
        }

        // The daemon catches up and stops, so the snapshot is stored with one posting
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(60.Seconds());
            await daemon.StopAllAsync();
        }

        // Two more postings the daemon has not seen
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new Bug6121NumberedLedgerPosted(), new Bug6121NumberedLedgerPosted());
            await session.SaveChangesAsync(token);
        }

        await using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Bug6121NumberedLedger>(stream, token)).ShouldNotBeNull().Postings.ShouldBe(1);
        }

        await using (var session = theStore.LightweightSession())
        {
            var writing = await session.Events.FetchForWriting<Bug6121NumberedLedger, Bug6121LedgerNumber>(number, token);
            writing.Aggregate.ShouldNotBeNull().Postings.ShouldBe(3);
            writing.CurrentVersion.ShouldBe(4);

            var latest = await session.Events.FetchLatest<Bug6121NumberedLedger, Bug6121LedgerNumber>(number, token);
            latest.ShouldNotBeNull().Postings.ShouldBe(3);
        }
    }

    [Fact]
    public async Task fetches_by_natural_key_fold_the_events_after_a_lagging_snapshot_with_string_streams()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<Bug6121KeyedLedger>(SnapshotLifecycle.Async);
        });

        var token = TestContext.Current.CancellationToken;
        var number = new Bug6121LedgerNumber($"L-{Guid.NewGuid():N}");
        var stream = $"ledger-{Guid.NewGuid():N}";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Bug6121KeyedLedger>(stream, new Bug6121NumberedLedgerOpened(number.Value),
                new Bug6121NumberedLedgerPosted());
            await session.SaveChangesAsync(token);
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(60.Seconds());
            await daemon.StopAllAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new Bug6121NumberedLedgerPosted(), new Bug6121NumberedLedgerPosted());
            await session.SaveChangesAsync(token);
        }

        await using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<Bug6121KeyedLedger>(stream, token)).ShouldNotBeNull().Postings.ShouldBe(1);
        }

        await using (var session = theStore.LightweightSession())
        {
            var writing = await session.Events.FetchForWriting<Bug6121KeyedLedger, Bug6121LedgerNumber>(number, token);
            writing.Aggregate.ShouldNotBeNull().Postings.ShouldBe(3);
            writing.CurrentVersion.ShouldBe(4);

            var latest = await session.Events.FetchLatest<Bug6121KeyedLedger, Bug6121LedgerNumber>(number, token);
            latest.ShouldNotBeNull().Postings.ShouldBe(3);
        }
    }
}
