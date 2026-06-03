using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c deferred — pin behavior of a user-defined raw
/// <see cref="IProjection"/> (the low-level interface that bypasses
/// aggregation / grouping machinery and writes directly through
/// <see cref="IDocumentOperations"/>) under <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// Raw IProjection is the escape hatch users reach for when none of the
/// higher-level projection bases fit — e.g., when they need to make their own
/// decisions about Store vs Update vs Delete per event. The pin: when run as
/// Inline under partitioning, the <see cref="IDocumentOperations"/> passed in
/// is the same tenant-scoped session that wrote the events, so doc writes
/// land in the correct tenant slot and the projection observes only events
/// from that single tenant per call.
/// </para>
///
/// <para>
/// Own-store because the raw projection is registered store-wide; using the
/// shared fixture would force every sibling test to carry the extra
/// projection registration.
/// </para>
/// </summary>
public class raw_iprojection_per_tenant : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_rawproj_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        // Reset cross-test capture before each store stands up so assertions
        // on TenantIdsSeen don't pick up sibling test data.
        TenantTouchProjection.TenantIdsSeen.Clear();
        TenantTouchProjection.BatchTenantIds.Clear();

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<TenantTouchEvent>();

            // Inline so the projection runs in the same session that's writing
            // the events — the tightest pin we can make for "the IDocumentOperations
            // passed in is tenant-scoped".
            opts.Projections.Add(new TenantTouchProjection(), ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task raw_iprojection_writes_doc_to_correct_tenant_slot_per_batch()
    {
        // Two tenants append the same kind of event. The raw projection writes
        // one TenantTouchDoc per stream id. After SaveChangesAsync, the doc
        // must be readable from that tenant's QuerySession and INVISIBLE from
        // the sibling tenant's session — proves IDocumentOperations.Store is
        // tenant-scoped under partitioning.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaStream,
                new TenantTouchEvent("alpha-1"), new TenantTouchEvent("alpha-2"));
            await session.SaveChangesAsync();
        }

        var betaStream = Guid.NewGuid();
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaStream, new TenantTouchEvent("beta-1"));
            await session.SaveChangesAsync();
        }

        // Each tenant's doc IS visible from its own session.
        await using (var alphaQuery = _store.QuerySession("alpha"))
        {
            var alphaDoc = await alphaQuery.LoadAsync<TenantTouchDoc>(alphaStream);
            alphaDoc.ShouldNotBeNull();
            alphaDoc!.TouchCount.ShouldBe(2);
        }
        await using (var betaQuery = _store.QuerySession("beta"))
        {
            var betaDoc = await betaQuery.LoadAsync<TenantTouchDoc>(betaStream);
            betaDoc.ShouldNotBeNull();
            betaDoc!.TouchCount.ShouldBe(1);
        }

        // Cross-tenant load returns null — proves the doc is in the writing
        // tenant's slot, not bleeding into the other.
        await using (var alphaQuery = _store.QuerySession("alpha"))
        {
            (await alphaQuery.LoadAsync<TenantTouchDoc>(betaStream))
                .ShouldBeNull("beta's doc must not be visible to alpha");
        }
        await using (var betaQuery = _store.QuerySession("beta"))
        {
            (await betaQuery.LoadAsync<TenantTouchDoc>(alphaStream))
                .ShouldBeNull("alpha's doc must not be visible to beta");
        }
    }

    [Fact]
    public async Task each_inline_call_sees_events_from_exactly_one_tenant()
    {
        // Inline projection slicing pin: when the raw IProjection.ApplyAsync
        // is called inline, every event in the batch carries the SAME TenantId
        // (the writing session's tenant). Cross-tenant fan-in does not happen
        // at the inline call site — that's an async-daemon-only concern.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(Guid.NewGuid(), new TenantTouchEvent("alpha-call"));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(Guid.NewGuid(), new TenantTouchEvent("beta-call"));
            await session.SaveChangesAsync();
        }

        // The capture list records the SET of tenant ids seen within each call
        // — every entry must have exactly one tenant id, never a mixed batch.
        TenantTouchProjection.BatchTenantIds.Count.ShouldBeGreaterThanOrEqualTo(2,
            "we appended two batches — the projection should have been called at least twice");

        foreach (var tenantsInBatch in TenantTouchProjection.BatchTenantIds)
        {
            tenantsInBatch.Count.ShouldBe(1,
                "inline projection call must see events from exactly one tenant per invocation — " +
                "received: " + string.Join(",", tenantsInBatch));
        }

        TenantTouchProjection.TenantIdsSeen.ShouldBe(new[] { "alpha", "beta" }, ignoreOrder: true);
    }
}

public record TenantTouchEvent(string Label);

public class TenantTouchDoc
{
    public Guid Id { get; set; }
    public int TouchCount { get; set; }
}

/// <summary>
/// Raw IProjection — the lowest-level projection surface. Receives an
/// <see cref="IDocumentOperations"/> + the batch's events and decides for
/// itself what to write. Cross-test telemetry is held in static collections
/// (reset at <c>InitializeAsync</c>) so we can pin both write-side correctness
/// AND the slicing contract of the inline call site.
/// </summary>
public class TenantTouchProjection : IProjection
{
    public static readonly HashSet<string> TenantIdsSeen = new();
    public static readonly List<HashSet<string>> BatchTenantIds = new();

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        // Capture every tenant id observed in this batch (for the slicing pin).
        var tenantsInThisBatch = new HashSet<string>();
        foreach (var e in events)
        {
            tenantsInThisBatch.Add(e.TenantId);
            TenantIdsSeen.Add(e.TenantId);
        }

        lock (BatchTenantIds)
        {
            BatchTenantIds.Add(tenantsInThisBatch);
        }

        // One TenantTouchDoc per stream, count = number of TenantTouchEvent in batch.
        foreach (var streamGroup in events.OfType<IEvent<TenantTouchEvent>>().GroupBy(e => e.StreamId))
        {
            operations.Store(new TenantTouchDoc
            {
                Id = streamGroup.Key,
                TouchCount = streamGroup.Count()
            });
        }

        return Task.CompletedTask;
    }
}
