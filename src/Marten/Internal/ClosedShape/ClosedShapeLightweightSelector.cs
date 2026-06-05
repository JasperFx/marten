#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Selectors;
using Marten.Internal.CodeGeneration;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Abstract base for the per-<see cref="ConcurrencyMode"/> closed-shape
/// Lightweight <see cref="ISelector{T}"/>. Owns the shared row-shape
/// (id at col 0, data at col 1, metadata at 2+) plus document
/// deserialization and metadata-apply. The per-row
/// <c>CaptureVersion</c> step is virtual; sealed subclasses provide a
/// monomorphic implementation so the JIT can devirtualize the hot
/// path (#4659).
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
    /// no-op; Optimistic captures the Guid into the per-type version
    /// dict; Numeric captures the long into the per-type revision dict.
    /// </summary>
    protected abstract void CaptureVersion(DbDataReader reader, TId id);

    protected T ReadDocument(DbDataReader reader)
    {
        if (_descriptor.HierarchyMapping is { } hierarchy)
        {
            var alias = reader.GetFieldValue<string>(FirstMetadataColumn + _descriptor.DocTypeReadIndex);
            return (T)_serializer.FromJson(hierarchy.TypeFor(alias), reader, DataColumn);
        }
        return _serializer.FromJson<T>(reader, DataColumn);
    }

    protected async System.Threading.Tasks.ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token)
    {
        if (_descriptor.HierarchyMapping is { } hierarchy)
        {
            var alias = await reader.GetFieldValueAsync<string>(FirstMetadataColumn + _descriptor.DocTypeReadIndex, token).ConfigureAwait(false);
            return (T)await _serializer.FromJsonAsync(hierarchy.TypeFor(alias), reader, DataColumn, token).ConfigureAwait(false);
        }
        return await _serializer.FromJsonAsync<T>(reader, DataColumn, token).ConfigureAwait(false);
    }

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
