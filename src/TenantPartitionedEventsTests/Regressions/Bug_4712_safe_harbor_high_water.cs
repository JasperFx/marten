#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #4712 — composite projection rebuilds hang under per-tenant event partitioning because the
/// store-global high-water detector computes a bogus <c>SafeHarborTime</c> of <c>0001-01-01</c>
/// (≈ <c>DateTime.MinValue</c> + the 3s stale threshold).
///
/// <para>
/// Root cause (the #4705 bug class, one query that was missed): the store-global
/// <see cref="HighWaterStatisticsDetector"/> reads <c>select last_value from mt_events_sequence</c>
/// for <c>HighestSequence</c>. Under <c>UseTenantPartitionedEvents</c> the store-global
/// <c>mt_events_sequence</c> is never advanced (each tenant draws seq_ids from its own
/// <c>mt_events_sequence_{suffix}</c>), so <c>HighestSequence</c> reads 1 while the real high-water
/// mark is far higher. The store-global agent then treats the store as perpetually <c>Stale</c> and,
/// because no store-global <c>HighWaterMark</c> progression row was read, leaves
/// <see cref="HighWaterStatistics.Timestamp"/> at <c>default(DateTimeOffset)</c> = 0001-01-01 — the
/// source of the bogus SafeHarborTime. The fix mirrors #4705: read <c>coalesce(max(seq_id),0)</c>
/// from <c>mt_events</c> under per-tenant partitioning, and always stamp <c>Timestamp</c>.
/// </para>
///
/// <para>Single-DB, single tenant — per-tenant partitioning is the only load-bearing factor (the
/// sharded multi-composite hang in the report is the downstream symptom of this same wrong reading).</para>
/// </summary>
public partial class Bug_4712_safe_harbor_high_water
{
    private readonly ITestOutputHelper _output;

    public Bug_4712_safe_harbor_high_water(ITestOutputHelper output) => _output = output;

    public class Bug4712Trip { public Guid Id { get; set; } public double Distance { get; set; } }

    public record Bug4712Started(Guid Id);
    public record Bug4712Leg(double Distance);

    public partial class Bug4712TripProjection: SingleStreamProjection<Bug4712Trip, Guid>
    {
        public Bug4712TripProjection() => Name = "Bug4712Trip";
        public void Apply(Bug4712Trip a, Bug4712Leg e) => a.Distance += e.Distance;
    }

    [Fact]
    public async Task high_water_detect_reports_true_max_sequence_and_a_real_timestamp()
    {
        using var store = (DocumentStore)DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = $"bug4712_p{Environment.ProcessId}";
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<Bug4712Trip>().DocumentAlias("b4712_trip");
            o.Projections.Add<Bug4712TripProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "t4712");

        long appended = 0;
        await using (var session = store.LightweightSession("t4712"))
        {
            for (var s = 0; s < 10; s++)
            {
                var streamId = Guid.NewGuid();
                session.Events.StartStream<Bug4712Trip>(streamId,
                    new Bug4712Started(streamId), new Bug4712Leg(1.0), new Bug4712Leg(2.0), new Bug4712Leg(3.0));
                appended += 4;
            }

            await session.SaveChangesAsync();
        }

        var database = (MartenDatabase)store.Storage.Database;
        var detector = new HighWaterDetector(database, store.Options.EventGraph, NullLogger.Instance);

        var statistics = await detector.Detect(CancellationToken.None);

        _output.WriteLine($"appended={appended}, HighestSequence={statistics.HighestSequence}, " +
                          $"CurrentMark={statistics.CurrentMark}, Timestamp={statistics.Timestamp:O}");

        // The high-water detector must see the real sequence height. Without the fix this reads 1
        // (the never-advanced store-global mt_events_sequence) and the store-global agent loops
        // forever in the Stale branch.
        statistics.HighestSequence.ShouldBe(appended);

        // And it must carry a real timestamp — a default(DateTimeOffset) here is exactly what yields
        // the 0001-01-01 SafeHarborTime that makes the gap-skip a no-op and hangs composite rebuilds.
        statistics.Timestamp.ShouldNotBe(default);
    }
}
