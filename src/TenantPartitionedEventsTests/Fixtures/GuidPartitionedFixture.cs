#nullable enable
using System;
using JasperFx.Events;
using Marten;

namespace TenantPartitionedEventsTests.Fixtures;

/// <summary>
/// Shared <see cref="DocumentStore"/> for guid-keyed (default) streams. Schema
/// name carries <see cref="Environment.ProcessId"/> so net9 + net10 in the same
/// database never race on partition / sequence names. DaemonLockId is a
/// fixed-but-unique-per-fixture value (distinct from the string fixture's) so
/// the two fixtures' daemons don't fight over the same advisory lock when both
/// collections happen to schedule a daemon test in parallel between assemblies.
/// </summary>
public sealed class GuidPartitionedFixture: PartitionedFixtureBase
{
    public const int DaemonLockId = 19981022;
    public string SchemaName { get; } = $"tp_events_guid_p{Environment.ProcessId}";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = SchemaName;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Projections.DaemonLockId = DaemonLockId;
    }
}

[Xunit.CollectionDefinition("guid-partitioned")]
public sealed class GuidPartitionedCollection: Xunit.ICollectionFixture<GuidPartitionedFixture>;
