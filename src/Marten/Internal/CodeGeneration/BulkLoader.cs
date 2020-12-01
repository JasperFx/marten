using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.CodeGeneration
{
    public abstract class BulkLoader<T, TId>: IBulkLoader<T>
    {
        private readonly IDocumentStorage<T, TId> _storage;

        public BulkLoader(IDocumentStorage<T, TId> storage)
        {
            _storage = storage;
        }

        public void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            using var writer = conn.BeginBinaryImport(MainLoaderSql());

            foreach (var document in documents)
            {
                _storage.AssignIdentity(document, tenant);
                writer.StartRow();
                LoadRow(writer, document, tenant, serializer);
            }

            writer.Complete();
        }

        public async Task LoadAsync(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CancellationToken cancellation)
        {
            using var writer = conn.BeginBinaryImport(MainLoaderSql());

            foreach (var document in documents)
            {
                _storage.AssignIdentity(document, tenant);
                await writer.StartRowAsync(cancellation);
                await LoadRowAsync(writer, document, tenant, serializer, cancellation);
            }

            await writer.CompleteAsync(cancellation);
        }

        public abstract void LoadRow(NpgsqlBinaryImporter writer, T document, ITenant tenant, ISerializer serializer);
        public abstract Task LoadRowAsync(NpgsqlBinaryImporter writer, T document, ITenant tenant, ISerializer serializer, CancellationToken cancellation);


        public abstract string MainLoaderSql();
        public abstract string TempLoaderSql();



        public abstract string CreateTempTableForCopying();

        public void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn,
            IEnumerable<T> documents)
        {
            using var writer = conn.BeginBinaryImport(TempLoaderSql());
            foreach (var document in documents)
            {
                writer.StartRow();
                LoadRow(writer, document, tenant, serializer);
            }

            writer.Complete();
        }

        public async Task LoadIntoTempTableAsync(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CancellationToken cancellation)
        {
            using var writer = conn.BeginBinaryImport(TempLoaderSql());
            foreach (var document in documents)
            {
                await writer.StartRowAsync(cancellation);
                await LoadRowAsync(writer, document, tenant, serializer, cancellation);
            }

            await writer.CompleteAsync(cancellation);
        }

        public abstract string CopyNewDocumentsFromTempTable();

        public abstract string OverwriteDuplicatesFromTempTable();
    }
}
