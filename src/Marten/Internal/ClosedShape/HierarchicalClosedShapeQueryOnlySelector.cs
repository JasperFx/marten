#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Hierarchical QueryOnly selector. Reads the <c>mt_doc_type</c> alias
/// from the descriptor's <see cref="DocumentStorageDescriptor{T,TId}.DocTypeReadIndex"/>
/// and dispatches deserialization through the
/// <see cref="DocumentStorageDescriptor{T,TId}.ResolveDocumentType"/> root —
/// the alias-to-.NET-type lookup the polymorphic JSON path needs.
/// #4659 Phase 2 leaf.
/// </summary>
internal sealed class HierarchicalClosedShapeQueryOnlySelector<T, TId>: ClosedShapeQueryOnlySelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Func<string, Type> _resolveType;
    private readonly int _docTypeOrdinal;

    public HierarchicalClosedShapeQueryOnlySelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        // The factory only constructs this leaf when ResolveDocumentType is
        // non-null; capture it once to avoid the per-row pattern-match.
        _resolveType = descriptor.ResolveDocumentType
            ?? throw new InvalidOperationException(
                "HierarchicalClosedShapeQueryOnlySelector requires a non-null descriptor.ResolveDocumentType; " +
                "the storage factory must dispatch to FlatClosedShapeQueryOnlySelector otherwise.");
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
