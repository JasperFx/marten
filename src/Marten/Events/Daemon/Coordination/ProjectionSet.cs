using System.Collections.Generic;
using Marten.Storage;

namespace Marten.Events.Daemon.Coordination;

public class ProjectionSet: IProjectionSet
{
    private readonly DocumentStore _store;
    private readonly MartenDatabase _database;

    public ProjectionSet(int lockId, DocumentStore store, MartenDatabase database, IReadOnlyList<ShardName> names)
    {
        _store = store;
        LockId = lockId;
        _database = database;
        Names = names;
    }

    public int LockId { get; }
    public IMartenDatabase Database => _database;
    public IProjectionDaemon BuildDaemon()
    {
        return _database.StartProjectionDaemon(_store);
    }

    public IReadOnlyList<ShardName> Names { get; }
}
