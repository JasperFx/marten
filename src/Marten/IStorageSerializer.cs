#nullable enable
using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Marten;

/// <summary>
///     Database-neutral serializer seam consumed by the closed-shape storage runtime (#4819,
///     follow-up to #4810). Exposes only the JSON read/write members the storage / binders /
///     selectors actually use, and — unlike <see cref="ISerializer"/> — carries no Npgsql-typed
///     or Marten-only members. The one write hook that used to bind an <c>NpgsqlParameter</c> is
///     surfaced here against the neutral <see cref="DbParameter"/>, mirroring the
///     <see cref="Marten.Internal.IStorageSession.ExecuteReaderAsync(DbCommand, CancellationToken)"/>
///     execution seam. Marten's <see cref="ISerializer"/> implements this; alternate dialects
///     (e.g. Polecat) implement it over their own provider types.
/// </summary>
public interface IStorageSerializer
{
    /// <summary>Serialize the document object into a JSON string.</summary>
    string ToJson(object? document);

    /// <summary>
    ///     Serialize <paramref name="value"/> directly into the supplied buffer writer as UTF-8 JSON —
    ///     the allocation-free append/write hot path.
    /// </summary>
    void WriteTo(IBufferWriter<byte> writer, object? value);

    /// <summary>Serialize a document without any extra type-handling metadata.</summary>
    string ToCleanJson(object? document);

    /// <summary>
    ///     Serialize <paramref name="value"/> as UTF-8 JSON and bind it to the supplied parameter
    ///     as JSONB. Db-neutral counterpart to <see cref="ISerializer.WriteToParameter"/> — the
    ///     concrete implementation binds against its provider's parameter type.
    /// </summary>
    void WriteToParameter(DbParameter parameter, object? value);

    /// <summary>Deserialize a JSON stream into an object of type T.</summary>
    T FromJson<T>(Stream stream);

    /// <summary>Deserialize the JSON at the reader's column index into an object of type T.</summary>
    T FromJson<T>(DbDataReader reader, int index);

    /// <summary>Deserialize a JSON stream into the supplied Type.</summary>
    object FromJson(Type type, Stream stream);

    /// <summary>Deserialize the JSON at the reader's column index into the supplied Type.</summary>
    object FromJson(Type type, DbDataReader reader, int index);

    /// <summary>Deserialize a JSON stream into an object of type T.</summary>
    ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>Deserialize the JSON at the reader's column index into an object of type T.</summary>
    ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default);

    /// <summary>Deserialize a JSON stream into the supplied Type.</summary>
    ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>Deserialize the JSON at the reader's column index into the supplied Type.</summary>
    ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index,
        CancellationToken cancellationToken = default);
}
