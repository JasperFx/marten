#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Microsoft.Extensions.Logging;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #5502 — <c>OperationPage</c> gained a per-statement <c>RecordsAffected</c> pass that reports a
/// projection-progression update which matched no row. Composite projections are the shape that puts
/// several progression operations in one batch; per-tenant partitioning is the other place batching
/// happens, so it needs its own pin.
///
/// <para>
/// It pins the opposite invariant. Per-tenant shards are NOT batched together: each
/// <c>{name}:All:{tenant}</c> shard gets its own <c>SubscriptionAgent</c>, its own
/// <c>ISubscriptionExecution</c> and therefore its own <c>ProjectionUpdateBatch</c> with exactly one
/// progression operation. Multi-tenancy inside a single batch is handled by
/// <c>ProjectionBatch.SessionForTenant</c>, which only ever adds DOCUMENT operations. If that ever
/// changes — if two tenants' progression writes start sharing a batch — this test's sibling
/// assumption breaks, and the #5502 hazard arrives in a second place.
/// </para>
///
/// <para>
/// The risk being covered either way is false positives: the tenant-bearing shard identity is what
/// the progression row is keyed on, so a daemon running cleanly across several tenants must report
/// nothing at all.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class per_tenant_progression_records_affected_check
{
    private readonly GuidPartitionedFixture _fixture;

    public per_tenant_progression_records_affected_check(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task a_clean_per_tenant_daemon_run_reports_no_progression_problems()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        await _fixture.AppendNEventsAsync(alpha, 3);
        await _fixture.AppendNEventsAsync(beta, 5);

        var recorder = new RecordingLogger();

        // The fixture's store is shared across this collection, and xUnit runs a collection's tests
        // sequentially, so borrowing the logger slot for the duration is safe. Restore it either way
        // so a failure here cannot leak into a sibling test.
        var previous = _fixture.Store.Options.DotNetLogger;
        _fixture.Store.Options.DotNetLogger = recorder;
        try
        {
            using var daemon = await _fixture.Store.BuildProjectionDaemonAsync();

            await daemon.RebuildProjectionAsync(
                TripDistanceProjection.ProjectionName, alpha, CancellationToken.None);
            await daemon.RebuildProjectionAsync(
                TripDistanceProjection.ProjectionName, beta, CancellationToken.None);
        }
        finally
        {
            _fixture.Store.Options.DotNetLogger = previous;
        }

        // Each tenant keeps its own progression row, keyed by the tenant-bearing shard identity...
        var progressions = await _fixture.Store.Advanced.AllProjectionProgress(
            token: TestContext.Current.CancellationToken);
        var names = progressions.Select(x => x.ShardName).ToArray();

        names.ShouldContain($"{TripDistanceProjection.ProjectionName}:All:{alpha}");
        names.ShouldContain($"{TripDistanceProjection.ProjectionName}:All:{beta}");

        // ...and a healthy run over both of them says nothing. A per-tenant progression write that
        // matched no row would show up here as a ProgressionProgressOutOfOrderException.
        recorder.ProgressionWarnings.ShouldBeEmpty(
            "a clean per-tenant daemon run must not report any progression problem");
    }

    private class RecordingLogger: ILogger
    {
        private readonly List<string> _warnings = new();

        public IReadOnlyList<string> ProgressionWarnings
        {
            get
            {
                lock (_warnings) return _warnings.ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            if (exception is not JasperFx.Events.Daemon.ProgressionProgressOutOfOrderException) return;

            lock (_warnings) _warnings.Add(formatter(state, exception));
        }
    }
}
