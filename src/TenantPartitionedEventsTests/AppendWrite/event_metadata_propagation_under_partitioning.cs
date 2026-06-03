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
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// #4617 section 3a — session-level metadata (CorrelationId, CausationId,
/// LastModifiedBy / user name, Headers) is stamped onto each event by
/// <c>QuickEventAppender.applyQuickMetadata</c> and carried through the
/// bulk <c>mt_quick_append_events</c> function into the per-tenant
/// partition. Per #4424 TenantPropagation, each event's <c>TenantId</c>
/// must match the stream's tenant — NOT the session's tenant — so a
/// session writing to multiple tenants in one save (uncommon but
/// supported) stamps each event with the right tenant. The shared
/// fixture's events table includes correlation_id, causation_id, headers,
/// and user_name columns by default — Marten registers all four metadata
/// types when the session's UserName / Correlation / Causation / Headers
/// properties are set on the session.
/// </summary>
[Collection("guid-partitioned")]
public class event_metadata_propagation_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public event_metadata_propagation_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task event_TenantId_equals_stream_TenantId_under_partitioning()
    {
        // #4424 TenantPropagation: every event's TenantId is the STREAM's tenant
        // (not the session's tenant — though in the simple single-tenant-session
        // case they're the same). Pin that the event reads back with the
        // expected tenant id, never null.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(streamId,
                new TripStarted(streamId), new TripLeg(1));
            await s.SaveChangesAsync();
        }

        await using var q = _fixture.Store.QuerySession(tenant);
        var events = await q.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);

        foreach (var e in events)
        {
            e.TenantId.ShouldBe(tenant,
                "every event's TenantId must equal the stream's tenant (TenantPropagation invariant)");
        }
    }
}

/// <summary>
/// Companion to <see cref="event_metadata_propagation_under_partitioning"/> for
/// the metadata columns Marten makes OPT-IN: <c>CorrelationId</c>,
/// <c>CausationId</c>, <c>Headers</c>, <c>UserName</c>. The shared
/// GuidPartitionedFixture doesn't enable them (default-off), so this test uses
/// its own DocumentStore with MetadataConfig flipped on. Pins that session-level
/// values propagate onto every event the bulk function inserts — and that they
/// stay paired with the right tenant_id in the partition.
/// </summary>
public class event_optional_metadata_propagation_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_meta_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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

            // Opt in to the four opt-in metadata columns.
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;

            opts.Events.AddEventType<MetaEvent>();
        });
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task session_metadata_propagates_to_each_event_in_the_tenants_partition()
    {
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var correlation = "corr-" + Guid.NewGuid().ToString("N")[..10];
        var causation = "caus-" + Guid.NewGuid().ToString("N")[..10];
        var userName = "user-" + Guid.NewGuid().ToString("N")[..8];

        var streamId = Guid.NewGuid();
        await using (var s = _store.LightweightSession("alpha"))
        {
            s.CorrelationId = correlation;
            s.CausationId = causation;
            s.LastModifiedBy = userName;

            s.Events.StartStream<MetaAggregate>(streamId,
                new MetaEvent("a"), new MetaEvent("b"), new MetaEvent("c"));
            await s.SaveChangesAsync();
        }

        await using var q = _store.QuerySession("alpha");
        var events = await q.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(3);

        foreach (var e in events)
        {
            e.CorrelationId.ShouldBe(correlation,
                "correlation id must propagate from the session onto every event in the bulk batch");
            e.CausationId.ShouldBe(causation,
                "causation id must propagate from the session onto every event in the bulk batch");
            e.UserName.ShouldBe(userName,
                "session.LastModifiedBy must propagate as the event's UserName column");
            e.TenantId.ShouldBe("alpha",
                "TenantId still propagates correctly alongside the opt-in metadata");
        }
    }
}

public record MetaEvent(string Label);

public class MetaAggregate
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public void Apply(MetaEvent e) => Label = e.Label;
}
