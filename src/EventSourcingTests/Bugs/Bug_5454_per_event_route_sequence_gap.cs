using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5454, the configurations #5456 left behind. #5456 routes a QuickWithServerTimestamps <c>StartStream</c> through
/// <c>mt_quick_append_events</c>, whose returned sequences make a rolled-back start tombstoneable, but it keeps the
/// per-event <c>InsertStream</c> + <c>QuickAppendEventWithVersion</c> route for <see cref="EventAppendMode.Quick"/>,
/// <c>UseArchivedStreamPartitioning</c> and <c>EnableStrictStreamIdentityEnforcement</c>. On that route each event's
/// <c>seq_id</c> is an inlined <c>nextval()</c> that nothing reads back, so a failure later in the same batch leaves
/// numbers no tombstone fills. A <c>FetchForWriting</c> against a missing stream is a Start too
/// (<c>StartEmptyStream</c>), so it takes the same route.
/// </summary>
public class Bug_5454_per_event_route_sequence_gap: OneOffConfigurationsContext
{
    private const string Tenant = "tenant_a";

    private async Task<DocumentStore> BuildStoreAsync(string configuration, string test)
    {
        var schema = $"b5454p_{test}_{configuration}";

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop schema if exists {schema} cascade").ExecuteNonQueryAsync();
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

            switch (configuration)
            {
                case "qwst":
                    // POSITIVE CONTROL: the configuration #5456 fixed. It must pass here, or the assertions below
                    // cannot see a tombstone at all.
                    break;
                case "quick":
                    opts.Events.AppendMode = EventAppendMode.Quick;
                    break;
                case "archived":
                    opts.Events.UseArchivedStreamPartitioning = true;
                    break;
                case "strict":
                    opts.Events.EnableStrictStreamIdentityEnforcement = true;
                    break;
                case "archived_strict":
                    opts.Events.UseArchivedStreamPartitioning = true;
                    opts.Events.EnableStrictStreamIdentityEnforcement = true;
                    break;
                case "archived_conjoined":
                    // A conjoined store with the exclusive append lock, the shape the field report came from.
                    opts.Events.UseArchivedStreamPartitioning = true;
                    opts.Events.UseExclusiveLockOnConcurrentAppends = true;
                    opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                    opts.Policies.AllDocumentsAreMultiTenanted();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration), configuration, null);
            }
        });

        _disposables.Add(store);
        return store;
    }

    private static IDocumentSession OpenSession(DocumentStore store) =>
        store.Options.Events.TenancyStyle == TenancyStyle.Conjoined
            ? store.LightweightSession(Tenant)
            : store.LightweightSession();

    private static async Task<Target> SeedTargetAsync(DocumentStore store)
    {
        var target = Target.Random();
        await using var seed = OpenSession(store);
        seed.Insert(target);
        await seed.SaveChangesAsync();
        return target;
    }

    private static async Task<(long lastValue, long maxSeq, long tombstones)> ReadSequenceStateAsync(
        DocumentStore store)
    {
        // #5688: tombstones are written off the failed save's path
        await store.Options.EventGraph.WaitForPendingTombstonesAsync();

        var schema = store.Options.Events.DatabaseSchemaName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var lastValue = Convert.ToInt64(
            await conn.CreateCommand($"select last_value from {schema}.mt_events_sequence").ExecuteScalarAsync());

        var maxObj = await conn.CreateCommand($"select max(seq_id) from {schema}.mt_events").ExecuteScalarAsync();
        var maxSeq = maxObj is null or DBNull ? 0L : Convert.ToInt64(maxObj);

        var tombstones = Convert.ToInt64(await conn
            .CreateCommand($"select count(*) from {schema}.mt_events where type = 'tombstone'")
            .ExecuteScalarAsync());

        return (lastValue, maxSeq, tombstones);
    }

    [Theory]
    [InlineData("qwst")]
    [InlineData("quick")]
    [InlineData("archived")]
    [InlineData("strict")]
    [InlineData("archived_strict")]
    [InlineData("archived_conjoined")]
    public async Task a_start_stream_whose_batch_fails_afterwards_leaves_no_unfilled_sequence_number(
        string configuration)
    {
        var store = await BuildStoreAsync(configuration, "start");

        // Its second INSERT fails the batch after the event inserts have drawn their sequence numbers
        // (UnitOfWork.AllOperations runs event operations first).
        var target = await SeedTargetAsync(store);

        await using (var session = OpenSession(store))
        {
            session.Events.StartStream<SimpleAggregate>(Guid.NewGuid(), new AEvent(), new BEvent());
            session.Insert(target);

            var ex = await Record.ExceptionAsync(() => session.SaveChangesAsync());
            ex.ShouldNotBeNull();
        }

        var (lastValue, maxSeq, tombstones) = await ReadSequenceStateAsync(store);

        // One assertion so a failure reports both halves: on the per-event route today it reads (0, 2), no tombstone
        // and two numbers drawn above the highest committed seq_id.
        (tombstones, lastValue - maxSeq).ShouldBe((2L, 0L),
            "(tombstones written, sequence numbers left unfilled above the highest committed seq_id)");
    }

    [Theory]
    [InlineData("qwst")]
    [InlineData("quick")]
    [InlineData("archived")]
    [InlineData("strict")]
    [InlineData("archived_strict")]
    [InlineData("archived_conjoined")]
    public async Task a_fetch_for_writing_on_a_missing_stream_whose_batch_fails_afterwards_leaves_no_unfilled_sequence_number(
        string configuration)
    {
        var store = await BuildStoreAsync(configuration, "fetch");
        var target = await SeedTargetAsync(store);

        await using (var session = OpenSession(store))
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(Guid.NewGuid());
            stream.Aggregate.ShouldBeNull();
            stream.AppendMany(new AEvent(), new BEvent());
            session.Insert(target);

            var ex = await Record.ExceptionAsync(() => session.SaveChangesAsync());
            ex.ShouldNotBeNull();
        }

        var (lastValue, maxSeq, tombstones) = await ReadSequenceStateAsync(store);

        // One assertion so a failure reports both halves: on the per-event route today it reads (0, 2), no tombstone
        // and two numbers drawn above the highest committed seq_id.
        (tombstones, lastValue - maxSeq).ShouldBe((2L, 0L),
            "(tombstones written, sequence numbers left unfilled above the highest committed seq_id)");
    }
}
