using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CommandLineRunner
{
    public class AsyncDaemonBootstrappingSamples
    {
        public async Task BootstrapSolo()
        {
            #region sample_bootstrap_daemon_solo

            var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.Connection("some connection string");

                        // Register any projections you need to run asynchronously
                        opts.Projections.Add<TripAggregationWithCustomName>(ProjectionLifecycle.Async);
                    })
                        // Turn on the async daemon in "Solo" mode
                        .AddAsyncDaemon(DaemonMode.Solo);
                })
                .StartAsync();

            #endregion
        }

        public async Task ErrorHandlingSolo()
        {

            var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.Connection("some connection string");

                        // Register any projections you need to run asynchronously
                        opts.Projections.Add<TripAggregationWithCustomName>(ProjectionLifecycle.Async);

                        #region sample_stop_shard_on_exception

                        // Stop only the current exception
                        opts.Projections.OnException<InvalidOperationException>()
                            .Stop();

                        // or get more granular
                        opts.Projections
                            .OnException<InvalidOperationException>(e => e.Message.Contains("Really bad!"))

                            .Stop(); // stops just the current projection shard

                        #endregion

                        #region sample_poison_pill

                        opts.Projections.OnApplyEventException()
                            .AndInner<ArithmeticException>()
                            .SkipEvent();

                        #endregion

                        #region sample_exponential_back-off_strategy

                        opts.Projections.OnException<NpgsqlException>()
                            .RetryLater(50.Milliseconds(), 250.Milliseconds(), 500.Milliseconds())
                            .Then
                            .Pause(1.Minutes());

                        #endregion
                    })
                        // Turn on the async daemon in "Solo" mode
                        .AddAsyncDaemon(DaemonMode.Solo);
                })
                .StartAsync();

        }



        public async Task BootstrapHotCold()
        {
            #region sample_bootstrap_daemon_hotcold

            var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.Connection("some connection string");

                        // Register any projections you need to run asynchronously
                        opts.Projections.Add<TripAggregationWithCustomName>(ProjectionLifecycle.Async);
                    })
                        // Turn on the async daemon in "HotCold" mode
                        // with built in leader election
                        .AddAsyncDaemon(DaemonMode.HotCold);
                })
                .StartAsync();

            #endregion


        }

        #region sample_DaemonDiagnostics

        public static async Task ShowDaemonDiagnostics(IDocumentStore store)
        {
            // This will tell you the current progress of each known projection shard
            // according to the latest recorded mark in the database
            var allProgress = await store.Advanced.AllProjectionProgress();
            foreach (var state in allProgress)
            {
                Console.WriteLine($"{state.ShardName} is at {state.Sequence}");
            }

            // This will allow you to retrieve some basic statistics about the event store
            var stats = await store.Advanced.FetchEventStoreStatistics();
            Console.WriteLine($"The event store highest sequence is {stats.EventSequenceNumber}");

            // This will let you fetch the current shard state of a single projection shard,
            // but in this case we're looking for the daemon high water mark
            var daemonHighWaterMark = await store.Advanced.ProjectionProgressFor(new ShardName(ShardState.HighWaterMark));
            Console.WriteLine($"The daemon high water sequence mark is {daemonHighWaterMark}");
        }

        #endregion

        #region sample_use_async_daemon_alone

        public static async Task UseAsyncDaemon(IDocumentStore store, CancellationToken cancellation)
        {
            using var daemon = store.BuildProjectionDaemon();

            // Fire up everything!
            await daemon.StartAllShards();


            // or instead, rebuild a single projection
            await daemon.RebuildProjection("a projection name", 5.Minutes(), cancellation);

            // or a single projection by its type
            await daemon.RebuildProjection<TripAggregationWithCustomName>(5.Minutes(), cancellation);

            // Be careful with this. Wait until the async daemon has completely
            // caught up with the currently known high water mark
            await daemon.WaitForNonStaleData(5.Minutes());

            // Start a single projection shard
            await daemon.StartShard("shard name", cancellation);

            // Or change your mind and stop the shard you just started
            await daemon.StopShard("shard name");

            // No, shut them all down!
            await daemon.StopAll();
        }

        #endregion
    }



}
