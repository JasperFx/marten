using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// #4617 section 3a — pin the timestamp source for each Quick append mode:
///
/// <list type="bullet">
///   <item><c>EventAppendMode.Quick</c> — the function uses server
///     <c>now() at time zone 'utc'</c> for every event's timestamp; caller's
///     <c>IEvent.Timestamp</c> is ignored.</item>
///   <item><c>EventAppendMode.QuickWithServerTimestamps</c> — despite the
///     name, the function honors the caller's <c>IEvent.Timestamp</c> via
///     the <c>timestamps[index]</c> array parameter.</item>
/// </list>
///
/// <para>
/// Each test builds its own store because <c>AppendMode</c> is a store-level
/// config switch. The Quick mode pin couldn't run on the shared
/// QuickWithServerTimestamps fixture without subverting it.
/// </para>
/// </summary>
public class timestamp_source_per_append_mode
{
    private static async Task<DocumentStore> BuildStoreAsync(EventAppendMode mode, string schema)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = mode;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<TickEvent>();
        });
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        return store;
    }

    [Fact]
    public async Task Quick_mode_uses_server_now_and_ignores_caller_timestamp()
    {
        var schema = $"tp_ts_q_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        await using var store = await BuildStoreAsync(EventAppendMode.Quick, schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "tq");

        // A caller-supplied timestamp deliberately set in 1900 — the Quick
        // function should IGNORE this and use server now() instead.
        var bogus = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var streamId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        await using (var session = store.LightweightSession("tq"))
        {
            // Use the IEvent envelope to set Timestamp explicitly.
            var envelope = new Event<TickEvent>(new TickEvent("alpha")) { Timestamp = bogus };
            session.Events.StartStream(streamId, envelope);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("tq");
        var fetched = await query.Events.FetchStreamAsync(streamId);
        var serverTs = fetched.Single().Timestamp;

        serverTs.Year.ShouldBeGreaterThan(2000,
            "Quick mode must IGNORE caller Timestamp and use server now() — observed year proves we got server time, not the 1900 sentinel");
        Math.Abs((serverTs - sentAtUtc).TotalMinutes).ShouldBeLessThan(5,
            "Quick mode's server now() must be close (within a few minutes) to when the test sent the append");
    }

    [Fact]
    public async Task QuickWithServerTimestamps_mode_honors_caller_supplied_timestamp()
    {
        var schema = $"tp_ts_qst_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        await using var store = await BuildStoreAsync(EventAppendMode.QuickWithServerTimestamps, schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "tqst");

        // Despite the mode's name, this branch HONORS the caller's Timestamp.
        var caller = new DateTimeOffset(2010, 6, 15, 12, 30, 0, TimeSpan.Zero);

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("tqst"))
        {
            var envelope = new Event<TickEvent>(new TickEvent("beta")) { Timestamp = caller };
            session.Events.StartStream(streamId, envelope);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("tqst");
        var fetched = await query.Events.FetchStreamAsync(streamId);
        var roundTripped = fetched.Single().Timestamp;

        // Postgres timestamp-with-time-zone has microsecond precision; the
        // round-trip should preserve the year/month/day exactly.
        roundTripped.Year.ShouldBe(2010);
        roundTripped.Month.ShouldBe(6);
        roundTripped.Day.ShouldBe(15);
    }
}

public record TickEvent(string Label);
