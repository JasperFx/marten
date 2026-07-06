#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Hierarchical, Optimistic Lightweight selector. #4659 Phase 2 leaf.
/// </summary>
internal sealed class HierarchicalOptimisticClosedShapeLightweightSelector<T, TId>: OptimisticClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Func<string, Type> _resolveType;
    private readonly int _docTypeOrdinal;

    public HierarchicalOptimisticClosedShapeLightweightSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        _resolveType = descriptor.ResolveDocumentType
            ?? throw new InvalidOperationException(
                "HierarchicalOptimisticClosedShapeLightweightSelector requires a non-null descriptor.ResolveDocumentType.");
        _docTypeOrdinal = FirstMetadataColumn + descriptor.DocTypeReadIndex;
    }

    protected override T ReadDocument(DbDataReader reader)
    {
        var alias = reader.GetFieldValue<string>(_docTypeOrdinal);
        return (T)_serializer.FromJson(_resolveType(alias), reader, DataColumn);
    }

    protected override async ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token)
    {
        var alias = await reader.GetFieldValueAsync<string>(_docTypeOrdinal, token).ConfigureAwait(false);
        return (T)await _serializer.FromJsonAsync(_resolveType(alias), reader, DataColumn, token).ConfigureAwait(false);
    }
}
