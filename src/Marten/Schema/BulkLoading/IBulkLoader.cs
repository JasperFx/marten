using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;

namespace Marten.Schema.BulkLoading
{
    public interface IBulkLoader<T>
    {
        void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);
        Task LoadAsync(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CancellationToken cancellation);

        string CreateTempTableForCopying();

        void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);
        Task LoadIntoTempTableAsync(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents, CancellationToken cancellation);

        string CopyNewDocumentsFromTempTable();

        string OverwriteDuplicatesFromTempTable();
    }
}
