#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Non-hierarchical, Off-mode IdentityMap selector. #4659 Phase 2 leaf.
/// </summary>
internal sealed class FlatUnversionedClosedShapeIdentityMapSelector<T, TId>: UnversionedClosedShapeIdentityMapSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    public FlatUnversionedClosedShapeIdentityMapSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
    }

    protected override T ReadDocument(DbDataReader reader)
        => _serializer.FromJson<T>(reader, DataColumn);

    protected override async ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token)
        => await _serializer.FromJsonAsync<T>(reader, DataColumn, token).ConfigureAwait(false);
}
