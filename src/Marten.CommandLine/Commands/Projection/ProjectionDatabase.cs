using Marten.CommandLine.Commands.Projection;
using Marten.Events.Daemon;
using Marten.Storage;

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
}
