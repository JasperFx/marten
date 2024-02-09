using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

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
        using var daemon = (ProjectionDaemon)database.BuildDaemon();
        await daemon.PrepareForRebuildsAsync().ConfigureAwait(false);

        var highWater = daemon.Tracker.HighWaterMark;
        if (highWater == 0)
        {
            return RebuildStatus.NoData;
        }

        var watcher = new RebuildWatcher(highWater);
        using var unsubscribe = daemon.Tracker.Subscribe(watcher);

        var watcherTask = watcher.Start();

        var projectionNames = asyncProjectionShards.Select(x => x.Name.ProjectionName).Distinct();

        var list = new List<Exception>();

        await Parallel.ForEachAsync(projectionNames, _cancellation.Token,
                async (projectionName, token) =>
                {
                    shardTimeout ??= 5.Minutes();

                    try
                    {
                        await daemon.RebuildProjection(projectionName, shardTimeout.Value, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Error while rebuilding projection {projectionName} on database '{database.Identifier}'[/]");
                        AnsiConsole.WriteException(e);
                        AnsiConsole.WriteLine();

                        list.Add(e);
                    }
                })
            .ConfigureAwait(false);

        await daemon.StopAllAsync().ConfigureAwait(false);

        watcher.Stop();
        await watcherTask.ConfigureAwait(false);

        if (list.Any())
        {
            throw new AggregateException(list);
        }

        return RebuildStatus.Complete;
    }

    public async Task StartShards(IProjectionDatabase database, IReadOnlyList<AsyncProjectionShard> shards)
    {
        var daemon = (ProjectionDaemon)database.BuildDaemon();
        var watcher = new DaemonWatcher(database.Parent.Name, database.Identifier, _statusGrid.Value);

        daemon.Tracker.Subscribe(watcher);

        foreach (var shard in shards)
        {
            await daemon.StartShard(shard.Name.Identity, _cancellation.Token).ConfigureAwait(false);
        }
    }

    public Task WaitForExit()
    {
        return _completion.Task;
    }
}
