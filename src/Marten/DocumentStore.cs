using System.Linq;
using FubuCore;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class DocumentStore : IDocumentStore
    {
        private readonly ICommandRunner _runner;
        private readonly ISerializer _serializer;

        public DocumentStore(IDocumentSchema schema, IDocumentCleaner cleaner, ICommandRunner runner, ISerializer serializer)
        {
            Schema = schema;
            _runner = runner;
            _serializer = serializer;

            Advanced = new AdvancedOptions(cleaner);

            // Not wanting Marten to be so dependent upon an IoC tool, so here's some poor man's DI
            Diagnostics = new Diagnostics(schema, new MartenQueryExecutor(_runner, schema, serializer, new MartenQueryParser()));
        }

        public IDocumentSchema Schema { get; }
        public AdvancedOptions Advanced { get; }


        public void BulkInsert<T>(T[] documents, int batchSize = 1000)
        {
            var storage = Schema.StorageFor(typeof(T)).As<IBulkLoader<T>>();

            _runner.ExecuteInTransaction(conn =>
            {
                if (documents.Length <= batchSize)
                {
                    storage.Load(_serializer, conn, documents);
                }
                else
                {
                    var total = 0;
                    var page = 0;

                    while (total < documents.Length)
                    {
                        var batch = documents.Skip(page * batchSize).Take(batchSize).ToArray();
                        storage.Load(_serializer, conn, batch);

                        page++;
                        total += batch.Length;
                    }
                }
            });
        }

        public IDiagnostics Diagnostics { get; }
    }
}