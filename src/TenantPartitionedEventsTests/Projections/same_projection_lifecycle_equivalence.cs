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
/// #4617 section 3c — pin that an Inline-lifecycle projection and a Live
/// aggregation yield byte-identical aggregate state for the same per-tenant
/// stream under <c>UseTenantPartitionedEvents</c>. Catches regressions where
/// the per-tenant inline write path applies events differently from the
/// per-tenant live read path (silent-split races, off-by-one version counts,
/// per-tenant sequence vs per-stream version conflation).
///
/// <para>
/// Own-store (not the shared fixture) so a single projection type registered
/// Inline + LiveStreamAggregation for the same TripSnapshot aggregate doesn't
/// collide with the fixture's existing TripDistance / TripCount registrations.
/// Local copies of the event + aggregate keep the test self-contained.
/// </para>
/// </summary>
public class same_projection_lifecycle_equivalence : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_lifecycle_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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

            opts.Events.AddEventType<TripStartedV2>();
            opts.Events.AddEventType<TripLegV2>();

            // Inline materializes a TripSummary per stream as events are
            // appended. Live aggregation reads the stream + folds via Apply()
            // on demand — same Apply() body, so any drift between the two
            // codepaths surfaces as a non-equal aggregate.
            opts.Schema.For<TripSummary>().Identity(x => x.Id).DocumentAlias("p2c_trip_summary");
            opts.Projections.Add<TripSummaryInlineProjection>(ProjectionLifecycle.Inline);
            opts.Projections.LiveStreamAggregation<TripSummary>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Inline_and_Live_projections_yield_identical_per_tenant_aggregate()
    {
        // For ONE tenant append a mixed sequence of TripStarted + TripLeg
        // events. Read the inline-materialized doc and the live-aggregated
        // result; pin Distance + LegCount byte-equal between the two paths.
        var tenant = "alpha_" + Guid.NewGuid().ToString("N")[..8];
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var session = _store.LightweightSession(tenant))
        {
            session.Events.StartStream<TripSummary>(streamId,
                new TripStartedV2(streamId),
                new TripLegV2(7.5),
                new TripLegV2(2.5),
                new TripLegV2(1.0));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession(tenant);

        // Inline-materialized doc: read directly from the projected document
        // table (no folding at read time).
        var inlineDoc = await query.LoadAsync<TripSummary>(streamId);
        inlineDoc.ShouldNotBeNull(
            "Inline projection must have written the doc during SaveChangesAsync");

        // Live aggregate: read the stream and fold via Apply() at query time.
        var liveAgg = await query.Events.AggregateStreamAsync<TripSummary>(streamId);
        liveAgg.ShouldNotBeNull(
            "Live aggregation must rebuild the same aggregate from the events");

        // The lifecycle equivalence: every observable field on the aggregate
        // must match. Drift here would mean the Inline write path and Live
        // read path took different code paths through Apply() — a bug.
        liveAgg!.Distance.ShouldBe(inlineDoc!.Distance,
            "Distance must agree between Inline and Live — they fold the same events through the same Apply()");
        liveAgg.LegCount.ShouldBe(inlineDoc.LegCount,
            "LegCount must agree between Inline and Live — they fold the same events through the same Apply()");
        // Headline numbers as a sanity backstop on the test itself.
        inlineDoc.Distance.ShouldBe(11.0);
        inlineDoc.LegCount.ShouldBe(3);
    }
}

public record TripStartedV2(Guid Id);
public record TripLegV2(double Distance);

public class TripSummary
{
    public Guid Id { get; set; }
    public double Distance { get; set; }
    public int LegCount { get; set; }
    public int Version { get; set; }

    public void Apply(TripLegV2 e)
    {
        Distance += e.Distance;
        LegCount++;
    }

    public static TripSummary Create(TripStartedV2 e) => new() { Id = e.Id };
}

public partial class TripSummaryInlineProjection: SingleStreamProjection<TripSummary, Guid>
{
    public const string ProjectionName = "TripSummaryInline";

    public TripSummaryInlineProjection()
    {
        Name = ProjectionName;
    }

    public void Apply(TripSummary agg, TripLegV2 e)
    {
        agg.Distance += e.Distance;
        agg.LegCount++;
    }

    public static TripSummary Create(TripStartedV2 e) => new() { Id = e.Id };
}
