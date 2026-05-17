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
/// W3 spike (M1+M2+M7): <see cref="ISelector{T}"/> for the lightweight
/// closed-shape storage path. Reads the data column at index 1 and
/// dispatches each <see cref="IDocumentMetadataBinder{TDoc}"/>.Apply at
/// the binder's column position (2, 3, …). Lightweight skips
/// identity-map writes — every <c>LoadAsync</c> hits the database.
/// Under <see cref="ConcurrencyMode.Optimistic"/> the selector also
/// captures each row's <c>mt_version</c> into <c>session.Versions</c>
/// so subsequent updates can supply it as the expected version.
/// </summary>
internal sealed class ClosedShapeLightweightSelector<T, TId>: ISelector<T>
    where T : notnull
    where TId : notnull
{
    // Lightweight column order from DocumentTable.SelectColumns:
    //   id (col 0), data (col 1), then ShouldSelect metadata columns.
    private const int IdColumn = 0;
    private const int DataColumn = 1;
    private const int FirstMetadataColumn = 2;

    private readonly ISerializer _serializer;
    private readonly DocumentStorageDescriptor<T, TId> _descriptor;
    private readonly Dictionary<TId, Guid>? _versions;
    private readonly Dictionary<TId, long>? _revisions;

    public ClosedShapeLightweightSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _serializer = session.Serializer;
        _descriptor = descriptor;
        _versions = descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic
            ? session.Versions.ForType<T, TId>()
            : null;
        _revisions = descriptor.ConcurrencyMode == ConcurrencyMode.Numeric
            ? session.Versions.RevisionsFor<T, TId>()
            : null;
    }

    public T Resolve(DbDataReader reader)
    {
        var doc = ReadDocument(reader);
        ApplyMetadata(reader, doc);
        CaptureVersion(reader);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var doc = await ReadDocumentAsync(reader, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        CaptureVersion(reader);
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

    private void CaptureVersion(DbDataReader reader)
    {
        var versionIndex = _descriptor.VersionReadIndex;
        if (versionIndex < 0) return;
        var versionOrdinal = FirstMetadataColumn + versionIndex;
        if (reader.IsDBNull(versionOrdinal)) return;

        if (_versions is not null)
        {
            var id = reader.GetFieldValue<TId>(IdColumn);
            _versions[id] = reader.GetFieldValue<Guid>(versionOrdinal);
        }
        else if (_revisions is not null)
        {
            var id = reader.GetFieldValue<TId>(IdColumn);
            _revisions[id] = reader.GetFieldValue<long>(versionOrdinal);
        }
    }
}
