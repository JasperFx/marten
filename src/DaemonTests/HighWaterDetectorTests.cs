using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Storage;
using Marten.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Weasel.Core;

namespace DaemonTests;

public class HighWaterDetectorTests: DaemonContext
{
    private readonly HighWaterDetector theDetector;

    public HighWaterDetectorTests(ITestOutputHelper output) : base(output)
    {
        theStore.EnsureStorageExists(typeof(IEvent));
        theDetector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events, NullLogger.Instance);
    }

    [Fact]
    public async Task find_all_zeros_with_no_state()
    {
        var statistics = await theDetector.Detect(CancellationToken.None);
        statistics.CurrentMark.ShouldBe(0);
        statistics.LastMark.ShouldBe(0);
        statistics.HighestSequence.ShouldBe(1);
        statistics.LastUpdated.ShouldBeNull();
    }

    [Fact]
    public async Task starting_from_first_detection_all_contiguous_events()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var statistics = await theDetector.Detect(CancellationToken.None);
        statistics.CurrentMark.ShouldBe(NumberOfEvents);
        statistics.LastMark.ShouldBe(0);
        statistics.HighestSequence.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task starting_from_first_detection_some_gaps_with_zero_buffer()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var gaps = new long[] {NumberOfEvents - 100, NumberOfEvents - 95, NumberOfEvents - 88};
        await deleteEvents(gaps);

        var statistics = await theDetector.Detect(CancellationToken.None);

        // This gets under the gap
        statistics.CurrentMark.ShouldBe(NumberOfEvents - 101);
        statistics.LastMark.ShouldBe(0);
        statistics.HighestSequence.ShouldBe(NumberOfEvents);
    }

    [Fact]
    public async Task second_run_detect_same_gap_when_stale()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var gaps = new long[] { NumberOfEvents - 100 };
        await deleteEvents(gaps);

        var statistics = await theDetector.Detect(CancellationToken.None);
        statistics.CurrentMark.ShouldBe(NumberOfEvents - 101);

        statistics = await theDetector.Detect(CancellationToken.None);
        statistics.CurrentMark.ShouldBe(NumberOfEvents - 101);
    }

    [Fact]
    public async Task starting_from_first_detection_some_gaps_with_nonzero_buffer()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var gaps = new long[] {NumberOfEvents - 100, NumberOfEvents - 95, NumberOfEvents - 88, NumberOfEvents - 33};
        await deleteEvents(gaps);



        var statistics = await theDetector.Detect(CancellationToken.None);

        // This gets under the gap, using the buffer
        statistics.CurrentMark.ShouldBe(NumberOfEvents - 101);
        statistics.HighestSequence.ShouldBe(NumberOfEvents);

        var statistics2 = await theDetector.DetectInSafeZone(CancellationToken.None);

        statistics2.CurrentMark.ShouldBe(NumberOfEvents - 96);
    }



    protected async Task deleteEvents(params long[] ids)
    {
        await using var conn = theStore.CreateConnection();
        await conn.OpenAsync();

        await conn
            .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
            .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
            .ExecuteNonQueryAsync();


    }

    protected async Task makeOldWhereSequenceIsLessThanOrEqualTo(long seqId)
    {
        await using var conn = theStore.CreateConnection();
        await conn.OpenAsync();

        await conn
            .CreateCommand($"update {theStore.Events.DatabaseSchemaName}.mt_events set timestamp = transaction_timestamp() - interval '1 hour' where seq_id <= :id")
            .With("id", seqId)
            .ExecuteNonQueryAsync();
    }

    protected async Task makeNewerWhereSequenceIsGreaterThan(long seqId)
    {
        await using var conn = theStore.CreateConnection();
        await conn.OpenAsync();

        await conn
            .CreateCommand($"update {theStore.Events.DatabaseSchemaName}.mt_events set timestamp = :timestamp where seq_id > :id")
            .With("id", seqId)
            .With("timestamp", DateTime.UtcNow.Add(30.Seconds()))
            .ExecuteNonQueryAsync();
    }
}
