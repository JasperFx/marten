using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c deferred — pin <c>AddGlobalProjection</c> behavior under
/// <c>UseTenantPartitionedEvents</c>: the projected aggregate doc keeps
/// <c>TenancyStyle.Single</c> while source events stay partitioned per tenant.
/// Sessions can read the global doc without scoping by tenant.
///
/// <para>
/// Own-store because <c>AddGlobalProjection</c> is store-config-level: it
/// registers a projection whose aggregate doc is explicitly opted-out of
/// the <c>AllDocumentsAreMultiTenanted</c> policy.
/// </para>
/// </summary>
public class add_global_projection_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_glob_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<GlobalTickEvent>();

            // The headline: register a SingleStreamProjection as GLOBAL — its
            // aggregate doc keeps TenancyStyle.Single even though the source
            // event store is partitioned per tenant and the default policy
            // multi-tenants every other doc.
            opts.Projections.AddGlobalProjection(new GlobalCounterProjection(), ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void global_projection_doc_is_TenancyStyle_Single_under_partitioning()
    {
        // Even with UseTenantPartitionedEvents on + AllDocumentsAreMultiTenanted,
        // the global projection's aggregate doc stays Single-tenanted by design.
        // Pin so a future change that auto-multi-tenants the global doc is a
        // deliberate contract change.
        _store.StorageFeatures.MappingFor(typeof(GlobalCounter)).TenancyStyle
            .ShouldBe(TenancyStyle.Single);
    }

    [Fact]
    public async Task append_to_global_aggregate_fails_with_MT002_under_partitioning_pin()
    {
        // The currently-broken surface: AddGlobalProjection's GlobalEventAppenderDecorator
        // writes the global aggregate's events to the *DEFAULT* tenant slot
        // (StorageConstants.DefaultTenantId). Under UseTenantPartitionedEvents,
        // every tenant must be registered as a Postgres partition suffix —
        // but `*DEFAULT*` contains characters PG identifiers can't carry, so
        // it can't be registered. Writing therefore fails with MT002 from the
        // mt_quick_append_events function.
        //
        // Pin this as a known-incompatible combination so a future fix —
        // either (a) routing global-aggregate events through a non-partitioned
        // sibling table, or (b) special-casing the default tenant inside the
        // bulk function — flips the assertion intentionally.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var globalId = Guid.NewGuid();

        await using var session = _store.LightweightSession("alpha");
        session.Events.StartStream<GlobalCounter>(globalId,
            new GlobalTickEvent("first"));

        var ex = await Should.ThrowAsync<Marten.Exceptions.MartenCommandException>(async () =>
            await session.SaveChangesAsync());
        ex.Message.ShouldContain("MT002");
        ex.Message.ShouldContain("*DEFAULT*");
    }
}

public record GlobalTickEvent(string Label);

public class GlobalCounter
{
    public Guid Id { get; set; }
    public int TickCount { get; set; }
}

public partial class GlobalCounterProjection : SingleStreamProjection<GlobalCounter, Guid>
{
    public GlobalCounterProjection() { Name = "GlobalCounter"; }
    public void Apply(GlobalCounter c, GlobalTickEvent _) => c.TickCount++;
}
