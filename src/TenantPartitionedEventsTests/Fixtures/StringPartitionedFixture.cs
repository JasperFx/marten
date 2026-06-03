#nullable enable
using System;
using JasperFx.Events;
using Marten;

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
}

[Xunit.CollectionDefinition("string-partitioned")]
public sealed class StringPartitionedCollection: Xunit.ICollectionFixture<StringPartitionedFixture>;
