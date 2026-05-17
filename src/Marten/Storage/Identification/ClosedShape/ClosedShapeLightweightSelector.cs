#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: <see cref="ISelector{T}"/> for the lightweight closed-shape
/// document storage. Reads the data column at index 1 — the
/// <c>DocumentTable.SelectColumns(StorageStyle.Lightweight)</c> column
/// order is <c>id, data</c>, so col 0 is the Guid id and col 1 is the
/// jsonb document body. Equivalent to what the existing codegen-emitted
/// <c>DocumentSelectorWithOnlySerializer</c> subclass produces for a
/// Lightweight + no-metadata document mapping.
/// </summary>
internal sealed class ClosedShapeLightweightSelector<T>: ISelector<T>
{
    private const int DataColumnIndex = 1;

    private readonly ISerializer _serializer;

    public ClosedShapeLightweightSelector(ISerializer serializer)
    {
        _serializer = serializer;
    }

    public T Resolve(DbDataReader reader)
        => _serializer.FromJson<T>(reader, DataColumnIndex);

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        => await _serializer.FromJsonAsync<T>(reader, DataColumnIndex, token).ConfigureAwait(false);
}
