using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Marten.Util;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class HighWaterDetectorTests: DaemonContext
    {
        private HighWaterDetector theDetector;

        public HighWaterDetectorTests()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
            theDetector = new HighWaterDetector(theStore.Tenancy.Default, theStore.Events);
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
        public async Task starting_from_first_detection_some_gaps_with_nonzero_buffer()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();

            var gaps = new long[] {NumberOfEvents - 100, NumberOfEvents - 95, NumberOfEvents - 88, NumberOfEvents - 33};
            await deleteEvents(gaps);



            var statistics = await theDetector.Detect(CancellationToken.None);

            // This gets under the gap, using the buffer
            statistics.CurrentMark.ShouldBe(NumberOfEvents - 101);
            statistics.LastMark.ShouldBe(0);
            statistics.HighestSequence.ShouldBe(NumberOfEvents);

            await makeOldWhereSequenceIsLessThanOrEqualTo(NumberOfEvents - 40);

            var safeTimestamp = statistics.LastUpdated.Value.Subtract(1.Seconds());
            var statistics2 = await theDetector.DetectInSafeZone(safeTimestamp, CancellationToken.None);

            statistics2.CurrentMark.ShouldBe(NumberOfEvents - 34);
        }



        protected async Task deleteEvents(params long[] ids)
        {
            using var conn = theStore.Tenancy.Default.CreateConnection();
            await conn.OpenAsync();

            await conn
                .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
                .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
                .ExecuteNonQueryAsync();


        }

        protected async Task makeOldWhereSequenceIsLessThanOrEqualTo(long seqId)
        {
            using var conn = theStore.Tenancy.Default.CreateConnection();
            await conn.OpenAsync();

            await conn
                .CreateCommand($"update {theStore.Events.DatabaseSchemaName}.mt_events set timestamp = transaction_timestamp() - interval '1 hour' where seq_id <= :id")
                .With("id", seqId, NpgsqlDbType.Bigint)
                .ExecuteNonQueryAsync();
        }

        protected async Task makeNewerWhereSequenceIsGreaterThan(long seqId)
        {
            using var conn = theStore.Tenancy.Default.CreateConnection();
            await conn.OpenAsync();

            await conn
                .CreateCommand($"update {theStore.Events.DatabaseSchemaName}.mt_events set timestamp = :timestamp where seq_id > :id")
                .With("id", seqId, NpgsqlDbType.Bigint)
                .With("timestamp", DateTime.UtcNow.Add(30.Seconds()))
                .ExecuteNonQueryAsync();
        }
    }
}
