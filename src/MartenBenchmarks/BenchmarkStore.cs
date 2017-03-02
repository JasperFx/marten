using Marten;
using Marten.Testing;
using Marten.Testing.CodeTracker;

namespace MartenBenchmarks
{
    public static class BenchmarkStore
    {
        public static readonly IDocumentStore Store;

        static BenchmarkStore()
        {
            Store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>();
                _.Events.AddEventType(typeof(Commit));
            });

            Store.Advanced.Clean.CompletelyRemoveAll();
            Store.Schema.ApplyAllConfiguredChangesToDatabase();
        }
    }
}