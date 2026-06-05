#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Hierarchical QueryOnly selector. Reads the <c>mt_doc_type</c> alias
/// from the descriptor's <see cref="DocumentStorageDescriptor{T,TId}.DocTypeReadIndex"/>
/// and dispatches deserialization through the
/// <see cref="DocumentStorageDescriptor{T,TId}.HierarchyMapping"/> root —
/// the alias-to-.NET-type lookup the polymorphic JSON path needs.
/// #4659 Phase 2 leaf.
/// </summary>
internal sealed class HierarchicalClosedShapeQueryOnlySelector<T, TId>: ClosedShapeQueryOnlySelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly DocumentMapping _hierarchy;
    private readonly int _docTypeOrdinal;

    public HierarchicalClosedShapeQueryOnlySelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        // The factory only constructs this leaf when HierarchyMapping is
        // non-null; capture it once to avoid the per-row pattern-match.
        _hierarchy = descriptor.HierarchyMapping
            ?? throw new InvalidOperationException(
                "HierarchicalClosedShapeQueryOnlySelector requires a non-null descriptor.HierarchyMapping; " +
                "the storage factory must dispatch to FlatClosedShapeQueryOnlySelector otherwise.");
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
