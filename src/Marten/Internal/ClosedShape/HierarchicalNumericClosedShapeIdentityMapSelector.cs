#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Hierarchical, Numeric IdentityMap selector. #4659 Phase 2 leaf.
/// </summary>
internal sealed class HierarchicalNumericClosedShapeIdentityMapSelector<T, TId>: NumericClosedShapeIdentityMapSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly DocumentMapping _hierarchy;
    private readonly int _docTypeOrdinal;

    public HierarchicalNumericClosedShapeIdentityMapSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        _hierarchy = descriptor.HierarchyMapping
            ?? throw new InvalidOperationException(
                "HierarchicalNumericClosedShapeIdentityMapSelector requires a non-null descriptor.HierarchyMapping.");
        _docTypeOrdinal = FirstMetadataColumn + descriptor.DocTypeReadIndex;
    }

    protected override T ReadDocument(DbDataReader reader)
    {
        var alias = reader.GetFieldValue<string>(_docTypeOrdinal);
        return (T)_serializer.FromJson(_hierarchy.TypeFor(alias), reader, DataColumn);
    }

    protected override async ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token)
    {
        var alias = await reader.GetFieldValueAsync<string>(_docTypeOrdinal, token).ConfigureAwait(false);
        return (T)await _serializer.FromJsonAsync(_hierarchy.TypeFor(alias), reader, DataColumn, token).ConfigureAwait(false);
    }
}
