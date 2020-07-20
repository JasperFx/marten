using System.Collections.Generic;
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
            using (var writer = conn.BeginBinaryImport(MainLoaderSql()))
            {
                foreach (var document in documents)
                {
                    _storage.AssignIdentity(document, tenant);
                    writer.StartRow();
                    LoadRow(writer, document, tenant, serializer);
                }

                writer.Complete();
            }
        }

        public abstract void LoadRow(NpgsqlBinaryImporter writer, T document, ITenant tenant, ISerializer serializer);


        public abstract string MainLoaderSql();
        public abstract string TempLoaderSql();



        public abstract string CreateTempTableForCopying();

        public void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn,
            IEnumerable<T> documents)
        {
            using (var writer = conn.BeginBinaryImport(TempLoaderSql()))
            {
                foreach (var document in documents)
                {
                    writer.StartRow();
                    LoadRow(writer, document, tenant, serializer);
                }

                writer.Complete();
            }
        }

        public abstract string CopyNewDocumentsFromTempTable();

        public abstract string OverwriteDuplicatesFromTempTable();
    }
}
