using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;

namespace Marten.Schema.BulkLoading;

/// <summary>
///     Internal service to implement bulk loading
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IBulkLoader<T>
{
    Task LoadAsync(Tenant tenant, ISerializer serializer, DbConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation);

    string CreateTempTableForCopying();

    Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, DbConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation);

    string CopyNewDocumentsFromTempTable();

    string OverwriteDuplicatesFromTempTable();

    string OverwriteDuplicatesFromTempTableWithVersionCheck();
}
