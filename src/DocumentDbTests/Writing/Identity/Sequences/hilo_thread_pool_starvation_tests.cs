using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class StarvationIntDoc
{
    public int Id { get; set; }
}

public class StarvationLongDoc
{
    public long Id { get; set; }
}

/// <summary>
///     #5969: the first burst of synchronous <c>Store()</c> calls on a HiLo-identified type used to stall the
///     whole process. One writer built the database's sequences by blocking a pool thread on the asynchronous
///     schema update, under a lock every other writer then blocked on, and that update needed a free pool thread
///     to finish. Nothing in the process ran until the thread pool had injected enough threads.
/// </summary>
[Collection(HiloThreadPoolCollection.Name)]
public class hilo_thread_pool_starvation_tests
{
    private readonly ITestOutputHelper _output;

    public hilo_thread_pool_starvation_tests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task the_first_burst_of_synchronous_stores_does_not_starve_the_thread_pool()
    {
        // A schema of its own, so the sequences really are cold: nothing has built mt_hilo or
        // mt_get_next_hi in it, in this process or any other
        var schema = "hilo_starve_" + Guid.NewGuid().ToString("N")[..10];

        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DisableNpgsqlLogging = true;
            opts.Schema.For<StarvationIntDoc>();
            opts.Schema.For<StarvationLongDoc>();
        });

        try
        {
            // Resolve the document storage up front. Only the sequences are left cold, so the burst measures
            // the HiLo path and nothing else that is built on first use
            store.Options.Providers.StorageFor<StarvationIntDoc>();
            store.Options.Providers.StorageFor<StarvationLongDoc>();

            // More writers than the pool has threads, so the pool cannot hand every one of them a thread
            // without injecting more. Each one blocks a pool thread in Store(), as application code does
            var writers = Math.Max(ThreadPool.ThreadCount, Environment.ProcessorCount) + 16;
            const int docsPerWriter = 5;

            using var stop = new CancellationTokenSource();
            var heartbeat = Task.Run(() => measureWorstHeartbeatDelayAsync(stop.Token));

            // Let the heartbeat settle into its loop before the writers arrive
            await Task.Delay(100);

            var ints = new ConcurrentBag<int>();
            var longs = new ConcurrentBag<long>();
            var failures = new ConcurrentBag<Exception>();

            var stopwatch = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
            {
                try
                {
                    using var session = store.LightweightSession();
                    for (var i = 0; i < docsPerWriter; i++)
                    {
                        var doc = new StarvationIntDoc();
                        session.Store(doc);
                        ints.Add(doc.Id);

                        var other = new StarvationLongDoc();
                        session.Store(other);
                        longs.Add(other.Id);
                    }
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            var burst = stopwatch.Elapsed;

            stop.Cancel();
            var worst = await heartbeat;

            _output.WriteLine(
                $"{writers} writers on {Environment.ProcessorCount} processors: burst took {burst.TotalMilliseconds:N0} ms, " +
                $"worst heartbeat delay {worst.TotalMilliseconds:N0} ms");

            failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures.Select(x => x.ToString())));

            // Every writer was given its ids while it was storing, and no two writers got the same one
            ints.Count.ShouldBe(writers * docsPerWriter);
            ints.ShouldAllBe(id => id > 0);
            ints.Distinct().Count().ShouldBe(ints.Count);
            longs.ShouldAllBe(id => id > 0);
            longs.Distinct().Count().ShouldBe(longs.Count);

            // A Task.Delay(10) heartbeat in the same process kept running while the writers were blocked.
            // Before #5969 it came back seconds late, because no pool thread was free to run it
            worst.ShouldBeLessThan(TimeSpan.FromMilliseconds(500),
                $"the thread pool was starved for {worst.TotalMilliseconds:N0} ms while {writers} writers stored their first documents");
        }
        finally
        {
            await dropSchemaAsync(schema);
        }
    }

    private static async Task<TimeSpan> measureWorstHeartbeatDelayAsync(CancellationToken token)
    {
        var worst = TimeSpan.Zero;
        var interval = TimeSpan.FromMilliseconds(10);
        var clock = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            var before = clock.Elapsed;
            await Task.Delay(interval, CancellationToken.None);
            var late = clock.Elapsed - before - interval;
            if (late > worst)
            {
                worst = late;
            }
        }

        return worst;
    }

    private static async Task dropSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"drop schema if exists {schema} cascade";
        await cmd.ExecuteNonQueryAsync();
    }
}
