using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;

namespace Marten.CommandLine.Commands.Projection;

public interface IProjectionStore
{
    string Name { get; }

    IReadOnlyList<AsyncProjectionShard> Shards { get; }

    ValueTask<IReadOnlyList<IProjectionDatabase>> BuildDatabases();

    DocumentStore InnerStore { get; }
}

public interface IProjectionDatabase
{
    IProjectionStore Parent { get; }

    string Identifier { get; }

    IProjectionDaemon BuildDaemon();
    Task AdvanceHighWaterMarkToLatestAsync(CancellationToken none);
}