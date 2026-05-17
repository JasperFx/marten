#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.Selectors;

namespace Marten.Storage.Identification.ClosedShape;

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
internal sealed class ClosedShapeIdentityMapSelector<T, TId>: ISelector<T>
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

        _versions = descriptor.ConcurrencyMode == ConcurrencyMode.Off
            ? null
            : session.Versions.ForType<T, TId>();
    }

    public T Resolve(DbDataReader reader)
    {
        var id = reader.GetFieldValue<TId>(IdColumn);
        var doc = _serializer.FromJson<T>(reader, DataColumn);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var id = await reader.GetFieldValueAsync<TId>(IdColumn, token).ConfigureAwait(false);
        var doc = await _serializer.FromJsonAsync<T>(reader, DataColumn, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        _identityMap[id] = doc;
        CaptureVersion(reader, id);
        return doc;
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
        if (_versions is null) return;
        var versionOrdinal = _descriptor.VersionReadOrdinal;
        if (versionOrdinal < 0 || reader.IsDBNull(versionOrdinal)) return;
        _versions[id] = reader.GetFieldValue<Guid>(versionOrdinal);
    }
}
