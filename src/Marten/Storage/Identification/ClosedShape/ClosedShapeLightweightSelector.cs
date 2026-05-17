#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M1): <see cref="ISelector{T}"/> for the lightweight
/// closed-shape document storage. Reads the data column at
/// <c>descriptor.DataColumnIndex</c> and dispatches each
/// <see cref="IDocumentMetadataBinder{TDoc}"/>.Apply at the binder's
/// column position. Equivalent to what the codegen-emitted
/// <c>DocumentSelectorWithOnlySerializer</c> + <c>DocumentSelectorWithVersions</c>
/// subclasses produce for a Lightweight document mapping.
/// </summary>
internal sealed class ClosedShapeLightweightSelector<T, TId>: ISelector<T>
    where T : notnull
    where TId : notnull
{
    private readonly ISerializer _serializer;
    private readonly DocumentStorageDescriptor<T, TId> _descriptor;

    public ClosedShapeLightweightSelector(ISerializer serializer, DocumentStorageDescriptor<T, TId> descriptor)
    {
        _serializer = serializer;
        _descriptor = descriptor;
    }

    public T Resolve(DbDataReader reader)
    {
        var doc = _serializer.FromJson<T>(reader, _descriptor.DataColumnIndex);
        ApplyMetadata(reader, doc);
        return doc;
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var doc = await _serializer.FromJsonAsync<T>(reader, _descriptor.DataColumnIndex, token).ConfigureAwait(false);
        ApplyMetadata(reader, doc);
        return doc;
    }

    private void ApplyMetadata(DbDataReader reader, T document)
    {
        // Metadata columns sit immediately after the data column.
        var ordinal = _descriptor.DataColumnIndex + 1;
        foreach (var binder in _descriptor.ReadBinders)
        {
            binder.Apply(reader, ordinal, document);
            ordinal++;
        }
    }
}
