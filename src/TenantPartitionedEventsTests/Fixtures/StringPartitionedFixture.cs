#nullable enable
using System;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;

namespace TenantPartitionedEventsTests.Fixtures;

/// <summary>
/// Shared <see cref="DocumentStore"/> for string-keyed streams. Mirrors
/// <see cref="GuidPartitionedFixture"/> with the only divergences that matter
/// at the schema level: <c>StreamIdentity.AsString</c> drives a <c>varchar</c>
/// `id` on `mt_streams` and the corresponding `streamid` argument type on the
/// generated quick-append function. A distinct DaemonLockId keeps advisory-lock
/// claims from the two fixtures non-overlapping.
/// </summary>
public sealed class StringPartitionedFixture: PartitionedFixtureBase
{
    public const int DaemonLockId = 19981023;
    public string SchemaName { get; } = $"tp_events_string_p{Environment.ProcessId}";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = SchemaName;
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Projections.DaemonLockId = DaemonLockId;
    }

    /// <summary>
    /// String-id parallels of the base's Guid-id projection set. The base's
    /// <c>TripDistance</c> / <c>TripCount</c> / <c>TripSnapshot</c> all carry a
    /// <c>Guid Id</c> field which would trip <c>ProjectionGraph.AssertValidity</c>
    /// ("Id type mismatch") under <see cref="StreamIdentity.AsString"/>. The
    /// string flavor registers <see cref="StringTripSnapshot"/> for the
    /// <c>FetchForWriting&lt;T&gt;</c> path the optimistic-concurrency tests
    /// exercise; the async/inline projections aren't needed by the current
    /// string-flavored test set so they're omitted to keep the registration
    /// surface minimal.
    /// </summary>
    protected override void RegisterProjections(StoreOptions opts)
    {
        opts.Projections.LiveStreamAggregation<StringTripSnapshot>();
    }
}

[Xunit.CollectionDefinition("string-partitioned")]
public sealed class StringPartitionedCollection: Xunit.ICollectionFixture<StringPartitionedFixture>;

// ---- String-id parallels of the base's Guid-id aggregate / event types ----
// Kept here (next to the StringPartitionedFixture) so the string-flavored
// projection registration is wired to types this file controls.

/// <summary>
/// String-id parallel of <see cref="TripSnapshot"/>. Self-aggregation target
/// for the <c>FetchForWriting&lt;StringTripSnapshot&gt;</c> tests under
/// <see cref="StreamIdentity.AsString"/>. Apply/Create follow the same
/// shape as the Guid version; only the <c>Id</c> field type differs.
/// </summary>
public class StringTripSnapshot
{
    public string Id { get; set; } = string.Empty;
    public double Distance { get; set; }
    public int LegCount { get; set; }
    public int Version { get; set; }

    public void Apply(StringTripLeg @event)
    {
        Distance += @event.Distance;
        LegCount++;
    }

    public static StringTripSnapshot Create(StringTripStarted @event) => new() { Id = @event.Id };
}

public record StringTripStarted(string Id);
public record StringTripLeg(double Distance);
