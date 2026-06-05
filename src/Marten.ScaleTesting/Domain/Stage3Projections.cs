// #4666 Phase B — stage-3 projections that read stage-2 AppointmentDetails +
// BoardSummary output. Two new multi-stream projections specifically built for
// the scaletesting harness; not lifted from DaemonTests (they don't exist there).
// Purpose: stress the (a) cross-stage Updated<TUpstream> chaining, (b) enrichment
// + reference-data joins under load, (c) per-tenant aggregation under conjoined
// tenancy.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.ScaleTesting.Domain;

// ---- ProviderUtilization ------------------------------------------------
//
// Per-provider rollup: how many appointments has each provider been assigned,
// how many have they completed, what's the average appointment-to-completion
// wall-time. Keyed by ProviderId; triggered by stage-2
// Updated<AppointmentDetails> emissions whose ProviderId is non-empty.
// Optionally enriched with the Provider reference document so the doc can
// carry the provider's display name without a second lookup at read time.

public class ProviderUtilization
{
    public Guid Id { get; set; } // = ProviderId
    public string ProviderFirstName { get; set; } = string.Empty;
    public string ProviderLastName { get; set; } = string.Empty;
    public ProviderRole ProviderRole { get; set; }
    public int AssignedCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
}

public partial class ProviderUtilizationProjection: MultiStreamProjection<ProviderUtilization, Guid>
{
    public ProviderUtilizationProjection()
    {
        Options.CacheLimitPerTenant = 500;

        // Slice key: ProviderId. Skip stage-2 updates that don't have a provider
        // assigned yet (the appointment may have been routed but not staffed).
        Identity<Updated<AppointmentDetails>>(x =>
            x.Entity.ProviderId == Guid.Empty ? Guid.Empty : x.Entity.ProviderId);

        // When the upstream Appointment is deleted (cancelled), surface that
        // so we can decrement appropriately.
        Identity<ProjectionDeleted<AppointmentDetails, Guid>>(x => x.Identity);
    }

    public override async Task EnrichEventsAsync(SliceGroup<ProviderUtilization, Guid> group,
        IQuerySession querySession, CancellationToken cancellation)
    {
        // One-shot lookup of the Provider doc per slice so the rollup picks up
        // the provider's name/role even if the upstream AppointmentDetails was
        // written before the Provider reference was materialised.
        await group
            .EnrichWith<Provider>()
            .ForEvent<Updated<AppointmentDetails>>()
            .ForEntityId(x => x.Entity.ProviderId)
            .AddReferences();
    }

    public override ProviderUtilization? Evolve(ProviderUtilization? snapshot, Guid id, IEvent e)
    {
        // Skip the synthetic "no provider yet" slice entirely.
        if (id == Guid.Empty) return snapshot;

        switch (e.Data)
        {
            case Updated<AppointmentDetails> updated:
                snapshot ??= new ProviderUtilization { Id = id };
                snapshot.AssignedCount++;
                if (updated.Entity.Status == AppointmentStatus.Completed)
                {
                    snapshot.CompletedCount++;
                }
                break;

            case References<Provider> provider:
                snapshot ??= new ProviderUtilization { Id = id };
                snapshot.ProviderFirstName = provider.Entity.FirstName;
                snapshot.ProviderLastName = provider.Entity.LastName;
                snapshot.ProviderRole = provider.Entity.Role;
                break;

            case ProjectionDeleted<AppointmentDetails>:
                // The upstream AppointmentDetails was deleted (cancelled
                // appointment). We can't tell from a delete event which
                // provider this referred to, so we let CancelledCount climb
                // at the slice we're already in.
                snapshot ??= new ProviderUtilization { Id = id };
                snapshot.CancelledCount++;
                break;
        }

        return snapshot;
    }
}

// ---- TenantBucketRollup -------------------------------------------------
//
// Per-tenant bucketed rollup of appointment activity. The bucket key is
// derived deterministically from the upstream AppointmentDetails id so the
// rollup is reproducible across rebuild runs (the validate / stress chain
// in Phase C wants byte-identical aggregates for the determinism gate).
// Roughly 64 buckets per tenant — enough cardinality to exercise the
// cross-stage chaining + per-tenant slice fan-out without exploding the
// projection table.
//
// Conjoined tenancy means each tenant sees its own slice of the projection
// table; keying on a derived bucket alone is fine because the tenant column
// filters reads. Originally drafted as a date-based rollup, but the
// DateTimeOffset.UtcNow fallback for stage-2 snapshots that didn't carry
// a Requested timestamp made rebuilds non-deterministic — the validate
// command caught it on first end-to-end run. Deterministic bucket key
// keeps the harness reproducible.

public class TenantBucketRollup
{
    [Identity]
    public string Bucket { get; set; } = string.Empty;
    public int RequestedCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }

    // SortedDictionary so the JSON serialization key order is stable across
    // rebuild runs — the validate / stress determinism gate hashes the data
    // column directly, so a Dictionary<,> with insertion-order serialization
    // would drift between runs.
    public SortedDictionary<string, int> ByRoutingReason { get; set; } = new();
}

public partial class TenantDailyRollupProjection: MultiStreamProjection<TenantBucketRollup, string>
{
    private const int BucketCount = 64;

    public TenantDailyRollupProjection()
    {
        Options.CacheLimitPerTenant = 100;
        Name = "TenantDailyRollup"; // preserves the projection name from the issue's spec

        Identity<Updated<AppointmentDetails>>(x => BucketKey(x.Entity.Id));
        Identity<ProjectionDeleted<AppointmentDetails, Guid>>(x => BucketKey(x.Identity));
    }

    public override TenantBucketRollup? Evolve(TenantBucketRollup? snapshot, string id, IEvent e)
    {
        switch (e.Data)
        {
            case Updated<AppointmentDetails> updated:
                snapshot ??= new TenantBucketRollup { Bucket = id };
                snapshot.RequestedCount++;
                if (updated.Entity.Status == AppointmentStatus.Completed)
                {
                    snapshot.CompletedCount++;
                }
                if (!string.IsNullOrEmpty(updated.Entity.RoutingReasonCode))
                {
                    snapshot.ByRoutingReason.TryGetValue(updated.Entity.RoutingReasonCode, out var current);
                    snapshot.ByRoutingReason[updated.Entity.RoutingReasonCode] = current + 1;
                }
                break;

            case ProjectionDeleted<AppointmentDetails>:
                snapshot ??= new TenantBucketRollup { Bucket = id };
                snapshot.CancelledCount++;
                break;
        }

        return snapshot;
    }

    /// <summary>
    /// Stable hash-bucket key for a Guid. We pull the first 4 bytes of the
    /// Guid into a uint and mod by <see cref="BucketCount"/>. Same Guid
    /// always lands in the same bucket → reproducible across rebuilds.
    /// </summary>
    private static string BucketKey(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var prefix = BitConverter.ToUInt32(bytes);
        return "b" + (prefix % BucketCount).ToString("D3");
    }
}
