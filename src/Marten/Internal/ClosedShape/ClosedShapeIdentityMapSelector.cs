#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Selectors;
using Marten.Internal.CodeGeneration;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M2): <see cref="ISelector{T}"/> for the
/// <see cref="IdentityMapSequentialGuidStorage{TDoc}"/> path. Reads
/// id+data+metadata and writes <c>(id, doc)</c> into the session's
/// identity-map dictionary so subsequent <c>LoadAsync</c> calls
/// short-circuit to the in-memory instance.
/// </summary>
/// <remarks>
/// The identity map lives on <see cref="IMartenSession.ItemMap"/> keyed by
/// document type. The selector acquires it (or creates a fresh one) at
/// construction so per-row writes don't re-walk the dictionary lookup.
/// Mirrors what the codegen-emitted <c>DocumentSelectorWithIdentityMap</c>
/// subclass does today.
/// </remarks>
internal sealed class ClosedShapeIdentityMapSelector<T, TId>: ISelector<T>, IDocumentSelector
    where T : notnull
    where TId : notnull
{
    // Same column layout as Lightweight: id at 0, data at 1, metadata at 2+.
    // IdColumn.ShouldSelect is true for all non-QueryOnly styles.
    private const int IdColumn = 0;
    private const int DataColumn = 1;
    private const int FirstMetadataColumn = 2;

    private readonly ISerializer _serializer;
    private readonly DocumentStorageDescriptor<T, TId> _descriptor;
    private readonly Dictionary<TId, T> _identityMap;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;

    public ClosedShapeIdentityMapSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
    {
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

        _versions = descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic
            ? session.Versions.ForType<T, TId>()
            : null;
        _revisions = descriptor.ConcurrencyMode == ConcurrencyMode.Numeric
            ? session.Versions.RevisionsFor<T, TId>()
            : null;
    }

    public T Resolve(DbDataReader reader)
    {
        var id = reader.GetFieldValue<TId>(IdColumn);
        var doc = ReadDocument(reader);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var id = await reader.GetFieldValueAsync<TId>(IdColumn, token).ConfigureAwait(false);
        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        return doc;
    }

    private T ReadDocument(DbDataReader reader)
    {
        if (_descriptor.HierarchyMapping is { } hierarchy)
        {
            var alias = reader.GetFieldValue<string>(FirstMetadataColumn + _descriptor.DocTypeReadIndex);
            return (T)_serializer.FromJson(hierarchy.TypeFor(alias), reader, DataColumn);
        }
        return _serializer.FromJson<T>(reader, DataColumn);
    }

    private async System.Threading.Tasks.ValueTask<T> ReadDocumentAsync(DbDataReader reader, CancellationToken token)
    {
        if (_descriptor.HierarchyMapping is { } hierarchy)
        {
            var alias = await reader.GetFieldValueAsync<string>(FirstMetadataColumn + _descriptor.DocTypeReadIndex, token).ConfigureAwait(false);
            return (T)await _serializer.FromJsonAsync(hierarchy.TypeFor(alias), reader, DataColumn, token).ConfigureAwait(false);
        }
        return await _serializer.FromJsonAsync<T>(reader, DataColumn, token).ConfigureAwait(false);
    }

    private void ApplyMetadata(DbDataReader reader, T document)
    {
        var ordinal = FirstMetadataColumn;
        foreach (var binder in _descriptor.ReadBinders)
        {
            binder.Apply(reader, ordinal, document);
            ordinal++;
        }
    }

    private void CaptureVersion(DbDataReader reader, TId id)
    {
        var versionIndex = _descriptor.VersionReadIndex;
        if (versionIndex < 0) return;
        var versionOrdinal = FirstMetadataColumn + versionIndex;
        if (reader.IsDBNull(versionOrdinal)) return;

        if (_versions is not null)
        {
            _versions[id] = reader.GetFieldValue<Guid>(versionOrdinal);
        }
        else if (_revisions is not null)
        {
            _revisions[id] = reader.GetFieldValue<long>(versionOrdinal);
        }
    }
}
