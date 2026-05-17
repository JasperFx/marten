#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.DirtyTracking;
using Marten.Linq.Selectors;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M2): <see cref="ISelector{T}"/> for the
/// <see cref="DirtyCheckedSequentialGuidStorage{TDoc}"/> path.
/// Identity-map writes (like <see cref="ClosedShapeIdentityMapSelector{T, TId}"/>)
/// plus a <see cref="ChangeTracker{T}"/> registered on the session for
/// every loaded document — gives <c>SaveChangesAsync</c> a baseline to
/// compare against when dirty-checking which loaded docs were modified.
/// </summary>
internal sealed class ClosedShapeDirtyTrackingSelector<T, TId>: ISelector<T>
    where T : notnull
    where TId : notnull
{
    private const int IdColumn = 0;
    private const int DataColumn = 1;
    private const int FirstMetadataColumn = 2;

    private readonly IMartenSession _session;
    private readonly ISerializer _serializer;
    private readonly DocumentStorageDescriptor<T, TId> _descriptor;
    private readonly Dictionary<TId, T> _identityMap;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;

    public ClosedShapeDirtyTrackingSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
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
        _session.ChangeTrackers.Add(new ChangeTracker<T>(_session, doc));
        CaptureVersion(reader, id);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var id = await reader.GetFieldValueAsync<TId>(IdColumn, token).ConfigureAwait(false);
        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        _session.ChangeTrackers.Add(new ChangeTracker<T>(_session, doc));
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
