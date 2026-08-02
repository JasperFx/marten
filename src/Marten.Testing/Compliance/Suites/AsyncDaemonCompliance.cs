using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Shouldly;
using Xunit;

namespace JasperFx.Events.ComplianceTests;

public record ItemAdded(string Name);

public record ItemRemoved(string Name);

/// <summary>
/// A deliberately plain self-aggregating snapshot type for the async daemon suite.
/// </summary>
public partial class DaemonItemTally
{
    public Guid Id { get; set; }
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }

    public void Apply(ItemAdded e) => AddedCount++;

    public void Apply(ItemRemoved e) => RemovedCount++;
}

/// <summary>
/// Proves the fixture's daemon plumbing end to end: an async snapshot projection registered through
/// <see cref="ComplianceStoreConfig"/>, driven by a started <see cref="Daemon.IProjectionDaemon"/>,
/// waited on through the store's non-stale hook, asserted against the *persisted* document, and
/// rebuilt once from the event stream.
/// </summary>
/// <remarks>
/// Deliberately small and single-tenant. Multi-node, HotCold and distribution behavior is
/// product-specific and stays out of compliance scope.
/// </remarks>
public abstract class AsyncDaemonCompliance<TFixture, TOperations, TQuerySession>
    : EventStoreComplianceSuite<TFixture, TOperations, TQuerySession>
    where TFixture : EventStoreComplianceFixture<TOperations, TQuerySession>, new()
    where TOperations : TQuerySession, IStorageOperations
{
    private static readonly Action<ComplianceStoreConfig> _configuration = config =>
    {
        config.SchemaName = "compliance_daemon";

        config.AddEventType<ItemAdded>();
        config.AddEventType<ItemRemoved>();

        config.Snapshot<DaemonItemTally>(SnapshotLifecycle.Async);
    };

    protected override Action<ComplianceStoreConfig> Configuration => _configuration;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task async_projection_catches_up_and_persists_the_document()
    {
        if (!theFixture.SupportsAsyncDaemon)
        {
            Assert.Skip("This event store does not support the async projection daemon in tests");
        }

        var streamId = Guid.NewGuid();

        await using (var session = OpenSession())
        {
            EventsFor(session).StartStream<DaemonItemTally>(streamId, new ItemAdded("one"), new ItemAdded("two"),
                new ItemRemoved("one"));
            await SaveChangesAsync(session);
        }

        await StartDaemonAsync();
        await WaitForNonStaleProjectionDataAsync(_timeout);

        await using var query = OpenSession();
        var tally = await LoadDocumentAsync<DaemonItemTally>(query, streamId);
        tally.ShouldNotBeNull();
        tally.AddedCount.ShouldBe(2);
        tally.RemovedCount.ShouldBe(1);
    }

    [Fact]
    public async Task rebuild_the_projection_from_the_event_stream()
    {
        if (!theFixture.SupportsAsyncDaemon)
        {
            Assert.Skip("This event store does not support the async projection daemon in tests");
        }

        var streamId = Guid.NewGuid();

        await using (var session = OpenSession())
        {
            EventsFor(session).StartStream<DaemonItemTally>(streamId, new ItemAdded("one"), new ItemAdded("two"));
            await SaveChangesAsync(session);
        }

        var daemon = await StartDaemonAsync();
        await WaitForNonStaleProjectionDataAsync(_timeout);

        await daemon.RebuildProjectionAsync<DaemonItemTally>(Cancellation);

        await using var query = OpenSession();
        var tally = await LoadDocumentAsync<DaemonItemTally>(query, streamId);
        tally.ShouldNotBeNull();
        tally.AddedCount.ShouldBe(2);
        tally.RemovedCount.ShouldBe(0);
    }
}
