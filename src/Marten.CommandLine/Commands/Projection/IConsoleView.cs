namespace Marten.CommandLine.Commands.Projection;

public interface IConsoleView
{
    void DisplayNoStoresMessage();
    void ListShards(IProjectionStore store);
    void DisplayEmptyEventsMessage(IProjectionStore store);
    string[] SelectStores(string[] storeNames);
    string[] SelectProjections(string[] projectionNames);
    void DisplayNoMatchingProjections();
    void WriteHeader(IProjectionStore store);
    void DisplayNoDatabases();
    void DisplayNoAsyncProjections();
    void WriteHeader(IProjectionDatabase database);
    string[] SelectDatabases(string[] databaseNames);
    void DisplayRebuildIsComplete();
}