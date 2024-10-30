using Marten.Events.Daemon;

namespace Marten.CommandLine.Commands.Projection;

public class DaemonStatusMessage
{
    public string StoreName { get; }
    public string DatabaseName { get; }
    public ShardState State { get; }

    public DaemonStatusMessage(string storeName, string databaseName, ShardState state)
    {
        StoreName = storeName;
        DatabaseName = databaseName;
        State = state;
    }
}