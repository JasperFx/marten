using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace DaemonTests;

public record Bug5055Event();

public class Bug5055Stream { public Guid Id { get; set; } }

public partial class Bug5055Projection: SingleStreamProjection<Bug5055Stream, Guid>
{
    public void Apply(Bug5055Event @event, Bug5055Stream projection) { }
}

public interface IBug5056Store: IDocumentStore;

// #5055/#5056 — at pod shutdown a second Pause/Stop pass over the coordinator fanned StopAllAsync out
// over daemons the first pass had already disposed, logging one ObjectDisposedException-backed
// "Error while trying to stop daemon agents" per daemon. The jasperfx#574 fix (2.36.2) makes the
// daemon stop path safe after disposal and adds the ClearResolvedDaemons() seam; the Marten side
// (#5056) clears the daemon cache on StopAsync and makes AddAsyncDaemon registration idempotent so
// the host can't end up stopping the same coordinator twice.
public class Bug_5055_5056_coordinator_double_stop: DaemonContext
{
    public Bug_5055_5056_coordinator_double_stop(ITestOutputHelper output): base(output)
    {
    }

    private const string Shard = "Bug5055Stream:All";

    [Fact]
    public async Task stopping_the_coordinator_twice_is_quiet_and_safe()
    {
        var logger = new CapturingLogger(_output);

        StoreOptions(x =>
        {
            x.Projections.Add(new Bug5055Projection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });

        var coordinator = new ProjectionCoordinator(theStore, logger);
        await coordinator.StartAsync(CancellationToken.None);

        try
        {
            // Get the coordinator genuinely running agents before shutting down
            await using (var session = theStore.LightweightSession())
            {
                for (var i = 0; i < 10; i++)
                {
                    session.Events.Append(Guid.NewGuid(), new Bug5055Event());
                }

                await session.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var daemon = coordinator.DaemonForMainDatabase();
            await daemon.Tracker.WaitForShardState(new ShardState(Shard, 10), 30.Seconds());
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }

        // The second pass is the #5055 shutdown shape (double hosted-service registration, user
        // pause + host stop, Wolverine quiesce + host stop). It must find nothing to stop.
        await coordinator.StopAsync(CancellationToken.None);

        logger.Entries.Where(x => x.Level == LogLevel.Error)
            .ShouldNotContain(x => x.Message.Contains("stop daemon agents"));
        logger.Entries.ShouldNotContain(x => x.Exception is ObjectDisposedException && x.Level == LogLevel.Error);
    }

    [Fact]
    public async Task daemon_accessors_after_stop_hand_back_fresh_daemons()
    {
        var logger = new CapturingLogger(_output);

        StoreOptions(x =>
        {
            x.Projections.Add(new Bug5055Projection(), ProjectionLifecycle.Async);
            x.Projections.AsyncMode = DaemonMode.Solo;
        });

        var coordinator = new ProjectionCoordinator(theStore, logger);

        var before = coordinator.DaemonForMainDatabase();
        await coordinator.StopAsync(CancellationToken.None);

        // jasperfx#574: the daemon StopAsync just disposed no-ops instead of throwing
        // ObjectDisposedException off its disposed CancellationTokenSource
        await before.StopAllAsync();

        // marten#5056: the cache was cleared, so the accessor rebuilds a fresh, usable daemon
        // instead of handing back the disposed instance
        var after = coordinator.DaemonForMainDatabase();
        ReferenceEquals(before, after).ShouldBeFalse();

        try
        {
            await after.StartAllAsync();
            await after.StopAllAsync();
        }
        finally
        {
            after.Dispose();
        }
    }

    [Fact]
    public void add_async_daemon_twice_registers_a_single_hosted_coordinator()
    {
        var once = registerMarten(1);
        var twice = registerMarten(2);

        twice.Count(x => x.ServiceType == typeof(Marten.Events.Daemon.Coordination.IProjectionCoordinator))
            .ShouldBe(1);
        twice.Count(x => x.ServiceType == typeof(IHostedService))
            .ShouldBe(once.Count(x => x.ServiceType == typeof(IHostedService)));
    }

    [Fact]
    public void add_async_daemon_twice_on_a_separate_store_registers_a_single_hosted_coordinator()
    {
        var once = registerMartenStore(1);
        var twice = registerMartenStore(2);

        twice.Count(x => x.ServiceType == typeof(Marten.Events.Daemon.Coordination.IProjectionCoordinator<IBug5056Store>))
            .ShouldBe(1);
        twice.Count(x => x.ServiceType == typeof(IHostedService))
            .ShouldBe(once.Count(x => x.ServiceType == typeof(IHostedService)));
    }

    private static IServiceCollection registerMarten(int addAsyncDaemonCalls)
    {
        var services = new ServiceCollection();
        var expression = services.AddMarten(opts => opts.Connection(ConnectionSource.ConnectionString));
        for (var i = 0; i < addAsyncDaemonCalls; i++)
        {
            expression.AddAsyncDaemon(DaemonMode.HotCold);
        }

        return services;
    }

    private static IServiceCollection registerMartenStore(int addAsyncDaemonCalls)
    {
        var services = new ServiceCollection();
        var expression = services.AddMartenStore<IBug5056Store>(opts =>
            opts.Connection(ConnectionSource.ConnectionString));
        for (var i = 0; i < addAsyncDaemonCalls; i++)
        {
            expression.AddAsyncDaemon(DaemonMode.HotCold);
        }

        return services;
    }

    // Records every log entry so the double-stop pass can be asserted quiet; TestLogger<T> only
    // echoes to output. Thread-safe: the coordinator loop and the test body log concurrently.
    private sealed class CapturingLogger: ILogger<ProjectionCoordinator>
    {
        private readonly ITestOutputHelper _output;
        private readonly object _gate = new();

        public CapturingLogger(ITestOutputHelper output) => _output = output;

        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (_gate)
            {
                Entries.Add((logLevel, message, exception));
            }

            _output.WriteLine($"{logLevel}: {message}");
            if (exception != null)
            {
                _output.WriteLine(exception.ToString());
            }
        }

        private sealed class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
