using Marten;
using Marten.Testing;
using Marten.Testing.CodeTracker;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

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
            Store.Schema.ApplyAllConfiguredChangesToDatabase().GetAwaiter().GetResult();
        }
    }
}
