using System.Diagnostics;
using System.Reflection;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// A launched <c>daemonload-node</c> child process for the marten#4883 multi-node scenario. Wraps
/// <see cref="Process"/> with the multi-node lifecycle the coordinator needs: launch + wait for
/// the node's <c>NODE_READY</c> handshake, request graceful stop by closing the child's stdin,
/// and kill outright to exercise leadership failover.
/// </summary>
internal sealed class NodeProcess: IDisposable
{
    private readonly Process _process;

    private NodeProcess(int nodeIndex, Process process)
    {
        NodeIndex = nodeIndex;
        _process = process;
        ApplicationName = MultiNodeStore.ApplicationNameForNode(nodeIndex);
    }

    public int NodeIndex { get; }
    public string ApplicationName { get; }
    public int Pid => _process.Id;

    /// <summary>
    /// Launch a node process of this same binary and wait for its stdout <c>NODE_READY</c> line so
    /// the coordinator only starts sampling once the daemon is actually up.
    /// </summary>
    public static async Task<NodeProcess> LaunchAsync(int nodeIndex, int projections)
    {
        // Re-invoke the CURRENT assembly via `dotnet <thisdll> daemonload-node ...`. Using the
        // managed dll path (not the apphost) keeps this portable across the way `dotnet run`
        // stages the build output.
        var entryAssembly = Assembly.GetEntryAssembly()!.Location;

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(entryAssembly);
        psi.ArgumentList.Add("daemonload-node");
        psi.ArgumentList.Add("--node");
        psi.ArgumentList.Add(nodeIndex.ToString());
        psi.ArgumentList.Add("--projections");
        psi.ArgumentList.Add(projections.ToString());

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to launch node {nodeIndex}");

        var node = new NodeProcess(nodeIndex, process);
        await node.WaitForReadyAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        return node;
    }

    private async Task WaitForReadyAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (line == null)
                {
                    throw new InvalidOperationException($"Node {NodeIndex} exited before signalling readiness");
                }

                if (line.StartsWith("NODE_READY", StringComparison.Ordinal))
                {
                    // Drain remaining stdout in the background so the child's pipe never blocks.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) != null)
                            {
                            }
                        }
                        catch
                        {
                            // pipe closed on exit
                        }
                    });
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Node {NodeIndex} did not signal readiness within {timeout.TotalSeconds:N0}s");
        }
    }

    /// <summary>Graceful stop: close the child's stdin so its stdin-EOF watcher shuts the host.</summary>
    public void RequestStop()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
            // already gone
        }
    }

    /// <summary>Kill outright — the leadership-failover exercise.</summary>
    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone
        }
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill();
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        _process.Dispose();
    }
}
