using System.Collections.Generic;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon.Coordination;

public class ProjectionSet: IProjectionSet
{
    private readonly MartenDatabase _database;

    public ProjectionSet(int lockId, MartenDatabase database, IReadOnlyList<ShardName> names)
    {
        LockId = lockId;
        _database = database;
        Names = names;
    }

    public int LockId { get; }
    public IProjectionDatabase Database => _database;
    public IReadOnlyList<ShardName> Names { get; }
}
