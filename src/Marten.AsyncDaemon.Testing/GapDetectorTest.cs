using System.Threading;
using System.Threading.Tasks;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Services;
using Marten.Testing;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class GapDetectorTest: DaemonContext
    {
        private readonly GapDetector theGapDetector;
        private readonly ISingleQueryRunner _runner;

        public GapDetectorTest(ITestOutputHelper output) : base(output)
        {
            theStore.EnsureStorageExists(typeof(IEvent));

            theGapDetector = new GapDetector(theStore.Events);
            _runner = new AutoOpenSingleQueryRunner(theStore.Tenancy.Default.Database);
        }

        [Fact]
        public async Task detect_first_gap()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();
            await deleteEvents(NumberOfEvents - 100, NumberOfEvents - 50);

            var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

            current.ShouldBe(NumberOfEvents - 101);
        }

        [Fact]
        public async Task detect_gap_if_gap_is_right_after_start()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();
            await deleteEvents(NumberOfEvents - 100, NumberOfEvents - 50);
            var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);
            theGapDetector.Start = current.Value;

            current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

            current.ShouldBe(NumberOfEvents - 101);
        }

        [Fact]
        public async Task get_max_seq_id_if_no_gap()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();

            var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

            current.ShouldBe(NumberOfEvents);
        }

        [Fact]
        public async Task get_max_seq_id_if_start_is_max_seq_id()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();
            theGapDetector.Start = NumberOfEvents;

            var current = await _runner.Query(theGapDetector, CancellationToken.None).ConfigureAwait(false);

            current.ShouldBe(NumberOfEvents);
        }

        protected async Task deleteEvents(params long[] ids)
        {
            using var conn = theStore.CreateConnection();
            await conn.OpenAsync();

            await conn
                .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
                .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
                .ExecuteNonQueryAsync();
        }
    }
}
