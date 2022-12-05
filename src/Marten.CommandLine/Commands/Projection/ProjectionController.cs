using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Events.Projections;

namespace Marten.CommandLine.Commands.Projection;

public class ProjectionController
{
    private readonly IProjectionHost _host;
    private readonly IConsoleView _view;

    public ProjectionController(IProjectionHost host, IConsoleView view)
    {
        _host = host;
        _view = view;
    }

    public async Task<bool> Execute(ProjectionInput input)
    {
        var stores = FilterStores(input);
        if (stores.IsEmpty())
        {
            _view.DisplayNoStoresMessage();
            return true;
        }

        if (input.ListFlag)
        {
            foreach (var store in stores)
            {
                _view.WriteHeader(store);
                _view.ListShards(store);
            }

            return true;
        }

        if (!input.RebuildFlag)
        {
            _host.ListenForUserTriggeredExit();

        }

        foreach (var store in stores)
        {
            if (store.Shards.IsEmpty()) break;


            var shards = FilterShards(input, store);
            if (shards.IsEmpty())
            {
                _view.WriteHeader(store);
                _view.DisplayNoMatchingProjections();
                _view.ListShards(store);
                break;
            }

            var databases = await store.BuildDatabases().ConfigureAwait(false);
            databases = FilterDatabases(input, databases);

            if (databases.IsEmpty())
            {
                _view.DisplayNoDatabases();
                break;
            }

            if (input.RebuildFlag)
            {
                _view.WriteHeader(store);
                foreach (var database in databases)
                {
                    if (databases.Count > 1)
                    {
                        _view.WriteHeader(database);
                    }

                    var status = await _host.TryRebuildShards(database, shards, input.ShardTimeout).ConfigureAwait(false);

                    if (status == RebuildStatus.NoData)
                    {
                        _view.DisplayEmptyEventsMessage(store);
                    }
                    else
                    {
                        _view.DisplayRebuildIsComplete();
                    }
                }
            }
            else
            {
                // Only run async shards here.
                shards = shards.Where(x => x.Source.Lifecycle == ProjectionLifecycle.Async).ToArray();
                if (shards.IsEmpty())
                {
                    _view.DisplayNoAsyncProjections();
                    _view.ListShards(store);
                    break;
                }

                foreach (var database in databases)
                {
                    await _host.StartShards(database, shards).ConfigureAwait(false);
                }

                await _host.WaitForExit().ConfigureAwait(false);
            }
        }

        return true;
    }


    public IReadOnlyList<IProjectionStore> FilterStores(ProjectionInput input)
    {
        var stores = _host.AllStores();

        if (input.StoreFlag.IsNotEmpty())
        {
            return stores.Where(x => x.Name.EqualsIgnoreCase(input.StoreFlag)).ToArray();
        }

        if (input.InteractiveFlag && stores.Count > 1)
        {
            var names = stores.Select(x => x.Name).OrderBy(x => x).ToArray();
            var selected = _view.SelectStores(names);
            return stores.Where(x => selected.Contains(x.Name)).ToArray();
        }

        return stores;
    }

    public IReadOnlyList<AsyncProjectionShard> FilterShards(ProjectionInput input, IProjectionStore store)
    {
        if (input.ProjectionFlag.IsNotEmpty())
        {
            return store.Shards.Where(x => x.Name.ProjectionName.EqualsIgnoreCase(input.ProjectionFlag))
                .ToArray();
        }

        if (input.InteractiveFlag && store.Shards.Count > 1)
        {
            var names = store.Shards
                .Select(x => x.Name.ProjectionName)
                .Distinct()
                .OrderBy(x => x).ToArray();

            var selected = _view.SelectProjections(names);

            return store.Shards.Where(x => selected.Contains(x.Name.ProjectionName))
                .ToArray();
        }

        return store.Shards;
    }

    public IReadOnlyList<IProjectionDatabase> FilterDatabases(ProjectionInput input, IReadOnlyList<IProjectionDatabase> databases)
    {
        if (input.DatabaseFlag.IsNotEmpty())
        {
            return databases.Where(x => x.Identifier.EqualsIgnoreCase(input.DatabaseFlag)).ToArray();
        }

        if (input.InteractiveFlag && databases.Count > 1)
        {
            var names = databases.Select(x => x.Identifier).OrderBy(x => x)
                .ToArray();

            var selected = _view.SelectDatabases(names);
            return databases.Where(x => selected.Contains(x.Identifier)).ToArray();
        }

        return databases;
    }
}
