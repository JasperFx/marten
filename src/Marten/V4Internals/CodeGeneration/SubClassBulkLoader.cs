using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Npgsql;

namespace Marten.V4Internals
{
    public class SubClassBulkLoader<T, TRoot>: IBulkLoader<T> where T : TRoot
    {
        private readonly IBulkLoader<TRoot> _inner;

        public SubClassBulkLoader(IBulkLoader<TRoot> inner)
        {
            _inner = inner;
        }

        public void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CharArrayTextWriter pool)
        {
            _inner.Load(tenant, serializer, conn, documents.OfType<TRoot>(), pool);
        }

        public string CreateTempTableForCopying()
        {
            return _inner.CreateTempTableForCopying();
        }

        public void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
            CharArrayTextWriter pool)
        {
            _inner.LoadIntoTempTable(tenant, serializer, conn, documents.OfType<TRoot>(), pool);
        }

        public string CopyNewDocumentsFromTempTable()
        {
            return _inner.CopyNewDocumentsFromTempTable();
        }

        public string OverwriteDuplicatesFromTempTable()
        {
            return _inner.OverwriteDuplicatesFromTempTable();
        }
    }
}
