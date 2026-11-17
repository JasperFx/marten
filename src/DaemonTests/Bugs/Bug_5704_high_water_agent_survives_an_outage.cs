using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5704: after a PostgreSQL outage in which connections HANG rather than being refused (a managed
/// database's maintenance window, or <c>docker pause</c>), the high-water agent stopped for good. Its
/// watchdog saw the poll loop go stale and called RestartAsync, which cleared IsRunning and stopped
/// the timer BEFORE re-detecting the mark. That Detect failed because the database was still down, and
/// nothing ever tried again: the old loop exited on its IsRunning check, the timer was never restarted,
/// and IsStale went false with IsRunning. The mark stayed frozen until the process was restarted.
///
/// <para>
/// Fixed upstream in JasperFx 2.91.0 (jasperfx#1027), which re-detects before retiring the running
/// loop, so a failed restart leaves the agent as it found it for the next health check to retry.
/// JasperFx's own test drives a fake detector. This one runs Marten's real detector against a real
/// database, through a proxy that reproduces the reporter's hang.
/// </para>
///
/// <para>
/// The timeouts are short so the outage can be too. What has to hold is the reporter's ordering: the
/// loop's in-flight Detect hangs for longer than the staleness threshold, so the watchdog restarts the
/// agent while the database is down, and the outage outlasts that restart's own Detect.
/// </para>
/// </summary>
public class Bug_5704_high_water_agent_survives_an_outage: DaemonContext
{
    public Bug_5704_high_water_agent_survives_an_outage(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public async Task the_high_water_mark_advances_again_after_an_outage_that_hangs_connections()
    {
        var target = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);
        await using var proxy = PausableTcpProxy.Start(target.Host!, target.Port);

        var throughProxy = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Host = "127.0.0.1",
            Port = proxy.Port,
            Timeout = 3,
            CommandTimeout = 6,
            CancellationTimeout = 1000
        };

        StoreOptions(opts =>
        {
            opts.Connection(throughProxy.ConnectionString);
            opts.Projections.HealthCheckPollingTime = 200.Milliseconds();
            opts.Projections.HighWaterStalenessThreshold = 1.Seconds();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await appendAnEvent();

        using var daemon = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync(logger: Logger);
        await daemon.StartHighWaterDetectionAsync();
        await daemon.Tracker.WaitForHighWaterMark(1, 15.Seconds());

        // The outage. Every connection, pooled or new, stops getting answers but stays open.
        proxy.Pause();
        await Task.Delay(15.Seconds());
        proxy.Resume();

        await appendAnEvent();

        await daemon.Tracker.WaitForHighWaterMark(2, 30.Seconds());
        daemon.Tracker.HighWaterMark.ShouldBe(2);

        await daemon.StopAllAsync();
    }

    private async Task appendAnEvent()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new TripStarted { Day = 1 });
        await session.SaveChangesAsync();
    }
}

/// <summary>
/// A TCP proxy that can stop forwarding without closing anything, which is what a paused container or
/// a stalled network path looks like from the client: connections stay open and simply stop answering,
/// and new ones are accepted and never completed. Bytes held during the pause are forwarded on resume.
/// </summary>
internal sealed class PausableTcpProxy: IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentBag<TcpClient> _clients = new();
    private volatile TaskCompletionSource _open;

    private PausableTcpProxy(string targetHost, int targetPort)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
        _open = opened();
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static PausableTcpProxy Start(string targetHost, int targetPort)
    {
        var proxy = new PausableTcpProxy(targetHost, targetPort);
        proxy._listener.Start();
        _ = proxy.acceptAsync();
        return proxy;
    }

    public void Pause() => _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Resume() => _open.TrySetResult();

    private static TaskCompletionSource opened()
    {
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        open.SetResult();
        return open;
    }

    private async Task acceptAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (Exception)
            {
                return;
            }

            _clients.Add(client);
            _ = relayAsync(client);
        }
    }

    private async Task relayAsync(TcpClient client)
    {
        using var server = new TcpClient();
        _clients.Add(server);

        try
        {
            // A connection opened during the pause is accepted and then left hanging
            await _open.Task.WaitAsync(_cancellation.Token);
            await server.ConnectAsync(_targetHost, _targetPort, _cancellation.Token);

            var fromClient = pumpAsync(client.GetStream(), server.GetStream());
            var fromServer = pumpAsync(server.GetStream(), client.GetStream());
            await Task.WhenAny(fromClient, fromServer);
        }
        catch (Exception)
        {
            // Either side went away, or the proxy is being disposed
        }
        finally
        {
            client.Dispose();
        }
    }

    private async Task pumpAsync(NetworkStream source, NetworkStream destination)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, _cancellation.Token);
            if (read == 0) return;

            await _open.Task.WaitAsync(_cancellation.Token);
            await destination.WriteAsync(buffer.AsMemory(0, read), _cancellation.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        foreach (var client in _clients) client.Dispose();
        _cancellation.Dispose();
        return ValueTask.CompletedTask;
    }
}
