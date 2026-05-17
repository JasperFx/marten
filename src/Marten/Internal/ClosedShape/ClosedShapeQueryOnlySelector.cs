#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M2): <see cref="ISelector{T}"/> for the
/// <see cref="QueryOnlySequentialGuidStorage{TDoc}"/> path. QueryOnly
/// storage excludes the id column from its SELECT projection (see
/// <c>IdColumn.ShouldSelect</c> — false for QueryOnly), so the data
/// column sits at index 0 and metadata starts at 1. No identity-map
/// writes — QueryOnly sessions don't track loaded docs.
/// </summary>
internal sealed class ClosedShapeQueryOnlySelector<T, TId>: ISelector<T>
    where T : notnull
    where TId : notnull
{
    private const int DataColumn = 0;
    private const int FirstMetadataColumn = 1;

    private readonly ISerializer _serializer;
    private readonly DocumentStorageDescriptor<T, TId> _descriptor;

    public ClosedShapeQueryOnlySelector(ISerializer serializer, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _serializer = serializer;
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
}
