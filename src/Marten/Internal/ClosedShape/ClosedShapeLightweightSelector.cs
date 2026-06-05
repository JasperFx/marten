#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Selectors;
using Marten.Internal.CodeGeneration;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Abstract base for the per-<see cref="ConcurrencyMode"/> ×
/// <see cref="DocumentStorageDescriptor{T,TId}.HierarchyMapping"/>
/// closed-shape Lightweight <see cref="ISelector{T}"/>. Owns the shared
/// row-shape (id at col 0, data at col 1, metadata at 2+) plus
/// metadata-apply. Sealed concurrency × hierarchy leaves provide
/// monomorphic <c>CaptureVersion</c> + <c>ReadDocument</c> /
/// <c>ReadDocumentAsync</c> bodies so the per-row hot path doesn't
/// branch on either descriptor field (#4659).
/// </summary>
internal abstract class ClosedShapeLightweightSelector<T, TId>: ISelector<T>, IDocumentSelector
    where T : notnull
    where TId : notnull
{
    // Lightweight column order from DocumentTable.SelectColumns:
    //   id (col 0), data (col 1), then ShouldSelect metadata columns.
    protected const int IdColumn = 0;
    protected const int DataColumn = 1;
    protected const int FirstMetadataColumn = 2;

    protected readonly IMartenSession _session;
    protected readonly ISerializer _serializer;
    protected readonly DocumentStorageDescriptor<T, TId> _descriptor;

    protected ClosedShapeLightweightSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _session = session;
        _serializer = session.Serializer;
        _descriptor = descriptor;
    }

    public T Resolve(DbDataReader reader)
    {
        var doc = ReadDocument(reader);
        ApplyMetadata(reader, doc);
        var id = _descriptor.Identification.ReadIdFromReader(reader, IdColumn);
        CaptureVersion(reader, id);
        _session.MarkAsDocumentLoaded(id, doc);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        var id = _descriptor.Identification.ReadIdFromReader(reader, IdColumn);
        CaptureVersion(reader, id);
        _session.MarkAsDocumentLoaded(id, doc);
        return doc;
    }

    /// <summary>
    /// Concurrency-specific per-row version capture. Off-mode subclasses
    /// no-op; Optimistic / Numeric capture into their typed tracker.
    /// </summary>
    protected abstract void CaptureVersion(DbDataReader reader, TId id);

    /// <summary>
    /// Hierarchy-specific per-row deserialization. Flat subclasses
    /// deserialize straight to <typeparamref name="T"/>; Hierarchical
    /// subclasses read <c>mt_doc_type</c> and dispatch through the
    /// hierarchy mapping. (#4659 Phase 2.)
    /// </summary>
    protected abstract T ReadDocument(DbDataReader reader);

    protected abstract ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token);

    protected void ApplyMetadata(DbDataReader reader, T document)
    {
        var ordinal = FirstMetadataColumn;
        foreach (var binder in _descriptor.ReadBinders)
        {
            binder.Apply(reader, ordinal, document, _session);
            ordinal++;
        }
    }
}
