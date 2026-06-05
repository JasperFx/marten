#nullable enable
using System.Collections.Generic;
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
/// closed-shape IdentityMap <see cref="ISelector{T}"/>. Owns the
/// identity-map cache init + lookup; sealed concurrency × hierarchy
/// leaves override <c>CaptureVersion</c> + <c>ReadDocument</c> so the
/// per-row hot path doesn't branch on either descriptor field (#4659).
/// </summary>
internal abstract class ClosedShapeIdentityMapSelector<T, TId>: ISelector<T>, IDocumentSelector
    where T : notnull
    where TId : notnull
{
    // Same column layout as Lightweight: id at 0, data at 1, metadata at 2+.
    protected const int IdColumn = 0;
    protected const int DataColumn = 1;
    protected const int FirstMetadataColumn = 2;

    protected readonly IMartenSession _session;
    protected readonly ISerializer _serializer;
    protected readonly DocumentStorageDescriptor<T, TId> _descriptor;
    protected readonly Dictionary<TId, T> _identityMap;

    protected ClosedShapeIdentityMapSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _session = session;
        _serializer = session.Serializer;
        _descriptor = descriptor;

        if (session.ItemMap.TryGetValue(typeof(T), out var existing))
        {
            _identityMap = (Dictionary<TId, T>)existing;
        }
        else
        {
            _identityMap = new Dictionary<TId, T>();
            session.ItemMap[typeof(T)] = _identityMap;
        }
    }

    public T Resolve(DbDataReader reader)
    {
        var id = _descriptor.Identification.ReadIdFromReader(reader, IdColumn);

        // Identity-map cache hit: return the previously loaded instance
        // instead of deserializing again. Subsequent Load<T>(id) calls
        // (including from CreateBatchQuery / batched LoadMany) get
        // reference-equal results back. Mirrors the codegen
        // DocumentSelectorWithIdentityMap.
        if (_identityMap.TryGetValue(id, out var cached))
        {
            CaptureVersion(reader, id);
            return cached;
        }

        var doc = ReadDocument(reader);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        _session.MarkAsDocumentLoaded(id, doc);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var id = _descriptor.Identification.ReadIdFromReader(reader, IdColumn);

        if (_identityMap.TryGetValue(id, out var cached))
        {
            CaptureVersion(reader, id);
            return cached;
        }

        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        _session.MarkAsDocumentLoaded(id, doc);
        return doc;
    }

    protected abstract void CaptureVersion(DbDataReader reader, TId id);

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
