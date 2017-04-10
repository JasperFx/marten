using System.Collections.Generic;
using Marten.Services;
using Npgsql;

namespace Marten.Schema.BulkLoading
{
    public interface IBulkLoader<T>
    {
        void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);
        void Load(DbObjectName table, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);

        string CreateTempTableForCopying();

        DbObjectName StorageTable { get; }
        void LoadIntoTempTable(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CharArrayTextWriter pool);
        string CopyNewDocumentsFromTempTable();
        string OverwriteDuplicatesFromTempTable();
    }
}