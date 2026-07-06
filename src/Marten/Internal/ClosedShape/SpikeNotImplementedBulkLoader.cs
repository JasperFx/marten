#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema.BulkLoading;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike: <see cref="IBulkLoader{T}"/> stub. Bulk-insert is out of spike
/// scope; <c>DocumentProvider&lt;T&gt;</c>'s constructor demands a non-null
/// instance, so this throws for every method. Replaced when bulk-insert
/// joins the closed-shape hierarchy.
/// </summary>
internal sealed class SpikeNotImplementedBulkLoader<T>: IBulkLoader<T>
{
    private const string Message =
        "Bulk insert isn't covered by the W3 spike's closed-shape document storage.";

    public Task LoadAsync(Tenant tenant, ISerializer serializer, DbConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation) => throw new NotSupportedException(Message);

    public string CreateTempTableForCopying() => throw new NotSupportedException(Message);

    public Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, DbConnection conn,
        IEnumerable<T> documents, CancellationToken cancellation) => throw new NotSupportedException(Message);

    public string CopyNewDocumentsFromTempTable() => throw new NotSupportedException(Message);

    public string OverwriteDuplicatesFromTempTable() => throw new NotSupportedException(Message);

    public string OverwriteDuplicatesFromTempTableWithVersionCheck() => throw new NotSupportedException(Message);
}
