using System.Threading;
using System.Threading.Tasks;
using Marten.CommandLine.Commands.Projection;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Microsoft.Extensions.Logging.Abstractions;

internal class ProjectionDatabase: IProjectionDatabase
{
    public ProjectionDatabase(IProjectionStore parent, MartenDatabase database)
    {
        Database = database;
        Parent = parent;
        Identifier = database.Identifier;
    }

    public MartenDatabase Database { get; }

    public IProjectionStore Parent { get; }
    public string Identifier { get; }
    public IProjectionDaemon BuildDaemon()
    {
        return Database.StartProjectionDaemon(Parent.InnerStore);
    }

    public Task AdvanceHighWaterMarkToLatestAsync(CancellationToken token)
    {
        var detector = new HighWaterDetector(Database, (EventGraph)Database.Options.Events, NullLogger.Instance);
        return detector.AdvanceHighWaterMarkToLatest(token);
    }
}
