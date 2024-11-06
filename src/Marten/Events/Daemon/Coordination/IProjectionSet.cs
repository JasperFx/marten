using System.Collections.Generic;
using JasperFx.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon.Coordination;

public interface IProjectionSet
{
    int LockId { get; }
    IMartenDatabase Database { get; }
    IProjectionDaemon BuildDaemon();
    IReadOnlyList<ShardName> Names { get; }
}