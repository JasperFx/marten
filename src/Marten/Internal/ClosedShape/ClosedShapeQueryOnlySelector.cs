#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Abstract base for the QueryOnly closed-shape <see cref="ISelector{T}"/>.
/// QueryOnly storage excludes the id column from its SELECT projection
/// (see <c>IdColumn.ShouldSelect</c> — false for QueryOnly), so the data
/// column sits at index 0 and metadata starts at 1. No identity-map
/// writes — QueryOnly sessions don't track loaded docs.
/// Sealed subclasses provide a monomorphic <c>ReadDocument</c> /
/// <c>ReadDocumentAsync</c> so the per-row hot path doesn't branch on
/// <c>ResolveDocumentType</c> (#4659 Phase 2).
/// </summary>
internal abstract class ClosedShapeQueryOnlySelector<T, TId>: ISelector<T>
    where T : notnull
    where TId : notnull
{
    protected const int DataColumn = 0;
    protected const int FirstMetadataColumn = 1;

    protected readonly IStorageSession _session;
    protected readonly IStorageSerializer _serializer;
    protected readonly DocumentStorageDescriptor<T, TId> _descriptor;

    protected ClosedShapeQueryOnlySelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _session = session;
        _serializer = session.Serializer;
        _descriptor = descriptor;
    }

    public T Resolve(DbDataReader reader)
    {
        var doc = ReadDocument(reader);
        ApplyMetadata(reader, doc);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        return doc;
    }

    protected abstract T ReadDocument(DbDataReader reader);

    protected abstract ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token);

    private void ApplyMetadata(DbDataReader reader, T document)
    {
        // #4602: QueryOnlyReadBinders, not ReadBinders — the QueryOnly SELECT omits
        // mt_version when the version/revision column has no member, so its binder
        // set is narrower to keep ordinals aligned with the projection.
        var ordinal = FirstMetadataColumn;
        foreach (var binder in _descriptor.QueryOnlyReadBinders)
        {
            binder.Apply(reader, ordinal, document, _session);
            ordinal++;
        }
    }
}
