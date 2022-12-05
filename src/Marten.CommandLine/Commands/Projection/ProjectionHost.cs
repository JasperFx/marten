using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Microsoft.Extensions.Hosting;

namespace Marten.CommandLine.Commands.Projection;

internal class ProjectionHost: IProjectionHost
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IHost _host;
    private readonly Lazy<DaemonStatusGrid> _statusGrid;

    public ProjectionHost(IHost host)
    {
        _host = host;

        _statusGrid = new Lazy<DaemonStatusGrid>(() =>
        {
            return new DaemonStatusGrid();
        });
    }

    public IReadOnlyList<IProjectionStore> AllStores()
    {
        return _host
            .AllDocumentStores()
            .OfType<DocumentStore>()
            .Select(x => new ProjectionStore(x))
            .ToList();
    }

    public void ListenForUserTriggeredExit()
    {
        var assembly = Assembly.GetEntryAssembly();
        AssemblyLoadContext.GetLoadContext(assembly).Unloading += context => Shutdown();

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            Shutdown();
            eventArgs.Cancel = true;
        };

        var shutdownMessage = "Press CTRL + C to quit";
        Console.WriteLine(shutdownMessage);
    }

    public void Shutdown()
    {
        _cancellation.Cancel();
        _completion.TrySetResult(true);
    }

    public async Task<RebuildStatus> TryRebuildShards(IProjectionDatabase database,
        IReadOnlyList<AsyncProjectionShard> asyncProjectionShards, TimeSpan? shardTimeout = null)
    {
        using var daemon = database.BuildDaemon();
        await daemon.StartDaemon().ConfigureAwait(false);

        var highWater = daemon.Tracker.HighWaterMark;
        if (highWater == 0)
        {
            return RebuildStatus.NoData;
        }

        // Just messes up the rebuild to have this going after the initial check
        await daemon.PauseHighWaterAgent().ConfigureAwait(false);

        var watcher = new RebuildWatcher(highWater);
        using var unsubscribe = daemon.Tracker.Subscribe(watcher);

        var watcherTask = watcher.Start();

        var projectionNames = asyncProjectionShards.Select(x => x.Name.ProjectionName).Distinct();

        await Parallel.ForEachAsync(projectionNames, _cancellation.Token,
                async (projectionName, token) =>
                {
                    if (shardTimeout == null)
                    {
                        await daemon.RebuildProjection(projectionName, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await daemon.RebuildProjection(projectionName, shardTimeout.Value, token).ConfigureAwait(false);
                    }
                })
            .ConfigureAwait(false);

        await daemon.StopAll().ConfigureAwait(false);

        watcher.Stop();
        await watcherTask.ConfigureAwait(false);

        return RebuildStatus.Complete;
    }

    public async Task StartShards(IProjectionDatabase database, IReadOnlyList<AsyncProjectionShard> shards)
    {
        var daemon = (ProjectionDaemon)database.BuildDaemon();
        var watcher = new DaemonWatcher(database.Parent.Name, database.Identifier, _statusGrid.Value);

        daemon.Tracker.Subscribe(watcher);

        foreach (var shard in shards)
        {
            await daemon.StartShard(shard, ShardExecutionMode.Continuous, _cancellation.Token).ConfigureAwait(false);
        }
    }

    public Task WaitForExit()
    {
        return _completion.Task;
    }
}
