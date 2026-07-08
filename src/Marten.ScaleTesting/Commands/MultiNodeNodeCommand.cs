using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events.Daemon;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// <c>daemonload-node</c>: the WORKER role of the marten#4883 multi-node daemonload scenario
/// (epic jasperfx#486 WS6). Launched as a child process by <c>daemonload-multinode</c>; builds the
/// shared tenant-partitioned store, joins native HotCold leadership contention via
/// <c>AddAsyncDaemon(DaemonMode.HotCold)</c>, and idles until the coordinator closes its stdin
/// (graceful stop) or kills the process (failover exercise).
///
/// The coordinator owns all schema/tenant provisioning and load; a node only runs the daemon, so
/// it never appends and never touches the schema — it just contends for and services the shard
/// agents that leadership hands it.
/// </summary>
[Description("Internal worker role for the multi-node daemonload scenario (marten#4883). Launched by `daemonload-multinode`; runs an IHost HotCold daemon node until stdin closes. Not intended to be run by hand.", Name = "daemonload-node")]
public sealed class MultiNodeNodeCommand: JasperFxAsyncCommand<MultiNodeNodeInput>
{
    public override async Task<bool> Execute(MultiNodeNodeInput input)
    {
        var applicationName = MultiNodeStore.ApplicationNameForNode(input.NodeFlag);

        using var host = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        if (input.DatabasesFlag > 1)
                        {
                            MultiNodeStore.ConfigureSharded(opts, input.ProjectionsFlag, input.DatabasesFlag, applicationName);
                        }
                        else
                        {
                            MultiNodeStore.Configure(opts, input.ProjectionsFlag, applicationName);
                        }
                    })
                    .AddAsyncDaemon(DaemonMode.HotCold);
            })
            .StartAsync()
            .ConfigureAwait(false);

        // Signal readiness on stdout so the coordinator can wait for the node to be up before
        // starting the clock / sampling. A single well-known line keeps the contract trivial.
        Console.WriteLine($"NODE_READY {input.NodeFlag} {applicationName}");
        Console.Out.Flush();

        // Idle until stdin closes (coordinator disposes the child's input stream on graceful stop)
        // or the process is killed outright (failover exercise). Reading stdin to EOF is the
        // cross-platform "parent asked me to stop" signal that needs no OS-specific IPC.
        using var shutdown = new CancellationTokenSource();
        var stdinWatcher = Task.Run(async () =>
        {
            try
            {
                using var stdin = Console.OpenStandardInput();
                var buffer = new byte[16];
                while (!shutdown.IsCancellationRequested)
                {
                    var read = await stdin.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break; // EOF — parent closed our stdin
                    }
                }
            }
            catch
            {
                // stdin unavailable / already closed — fall through to shutdown
            }
            finally
            {
                shutdown.Cancel();
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // graceful stop requested
        }

        AnsiConsole.MarkupLine($"[grey]node {input.NodeFlag} shutting down[/]");
        return true;
    }
}
