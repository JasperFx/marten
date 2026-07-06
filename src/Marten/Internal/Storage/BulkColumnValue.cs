#nullable enable

namespace Marten.Internal.Storage;

/// <summary>
///     #4828: a single column's contribution to a bulk-load row — the value plus its dialect-neutral
///     <see cref="StorageColumnType"/>. Returned by <c>IDocumentMetadataBinder.GetBulkValue</c> so the
///     dialect-specific bulk loader can write it with its native mechanism, replacing the binders'
///     direct <c>NpgsqlBinaryImporter</c> coupling.
/// </summary>
/// <remarks>
///     <see cref="Value"/> semantics:
///     <list type="bullet">
///         <item><c>null</c> → write an untyped SQL NULL.</item>
///         <item><see cref="System.DBNull"/> → write a typed NULL of <see cref="Type"/>.</item>
///         <item>otherwise → write the value as <see cref="Type"/>.</item>
///     </list>
/// </remarks>
public readonly struct BulkColumnValue
{
    /// <summary>An untyped NULL (the loader writes a plain NULL, no provider type).</summary>
    public static readonly BulkColumnValue Null = new(null, default);

    public BulkColumnValue(object? value, StorageColumnType type)
    {
        Value = value;
        Type = type;
    }

    public object? Value { get; }

    public StorageColumnType Type { get; }

    /// <summary>A typed NULL of the given column type (e.g. a JSONB null).</summary>
    public static BulkColumnValue TypedNull(StorageColumnType type) => new(System.DBNull.Value, type);
}
