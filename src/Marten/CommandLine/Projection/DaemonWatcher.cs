using System;
using Marten.Events.Daemon;

namespace Marten.CommandLine.Projection;

internal class DaemonWatcher : IObserver<ShardState>
{
    private readonly string _storeName;
    private readonly string _databaseName;
    private readonly DaemonStatusGrid _grid;

    public DaemonWatcher(string storeName, string databaseName, DaemonStatusGrid grid)
    {
        _storeName = storeName;
        _databaseName = databaseName;
        _grid = grid;
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(ShardState value)
    {
        _grid.Post(new DaemonStatusMessage(_storeName, _databaseName, value));
    }
}