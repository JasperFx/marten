using System.Collections.Generic;
using Npgsql;

namespace Marten.Schema.BulkLoading
{
    public interface IBulkLoader<T>
    {
        void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);
        void Load(TableName table, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);

        string CreateTempTableForCopying();

        TableName StorageTable { get; }
        void LoadIntoTempTable(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);
        string CopyNewDocumentsFromTempTable();
        string OverwriteDuplicatesFromTempTable();
    }
}