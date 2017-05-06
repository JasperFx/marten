using System.Collections.Generic;
using Marten.Services;
using Marten.Storage;
using Npgsql;

namespace Marten.Schema.BulkLoading
{
    public interface IBulkLoader<T>
    {
        void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);
        void Load(ITenant tenant, DbObjectName table, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);

        string CreateTempTableForCopying();

        DbObjectName StorageTable { get; }
        void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);
        string CopyNewDocumentsFromTempTable();
        string OverwriteDuplicatesFromTempTable();
    }
}