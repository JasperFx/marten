#nullable enable
using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Weasel.Core;

namespace Marten;

#region sample_iserializer

/// <summary>
///     When selecting data through Linq Select() transforms,
///     should the data elements returned from Postgresql be
///     cast to their raw types or simple strings
/// </summary>
public enum ValueCasting
{
    /// <summary>
    ///     Json fields will be returned with their values cast to
    ///     the proper type. I.e., {"number": 1}
    /// </summary>
    Strict,

    /// <summary>
    ///     Json fields will be returned with their values in simple
    ///     string values. I.e., {"number": "1"}
    /// </summary>
    Relaxed
}

public interface ISerializer: IStorageSerializer
{
    // #4819: the db-neutral WriteToParameter(DbParameter) from IStorageSerializer bridges to
    // Marten's Npgsql-typed WriteToParameter below. Every Marten serializer is Npgsql-backed, so
    // the parameter is always an NpgsqlParameter at runtime.
    void IStorageSerializer.WriteToParameter(DbParameter parameter, object? value)
        => WriteToParameter((NpgsqlParameter)parameter, value);

    /// <summary>
    ///     Just gotta tell Marten if enum's are stored
    ///     as int's or string's in the JSON
    /// </summary>
    EnumStorage EnumStorage { get; }

    /// <summary>
    ///     Specify whether properties in the JSON document should use Camel or Pascal casing.
    /// </summary>
    Casing Casing { get; }

    /// <summary>
    ///     Controls how the Linq Select() behavior needs to work in the database
    /// </summary>
    ValueCasting ValueCasting { get; }

    /// <summary>
    ///     Convenience for the most common append-path use of <see cref="IStorageSerializer.WriteTo"/>: serialize
    ///     <paramref name="value"/> as UTF-8 JSON and bind the resulting bytes to <paramref name="parameter"/>
    ///     with <c>NpgsqlDbType.Jsonb</c>. Skips the round-trip through a .NET <see cref="string"/> that
    ///     <c>AppendParameter(ToJson(value))</c> incurs.
    /// </summary>
    /// <param name="parameter">The Npgsql parameter the JSON bytes will bind to.</param>
    /// <param name="value">The value to serialize; <c>null</c> binds <see cref="DBNull"/>.</param>
    void WriteToParameter(NpgsqlParameter parameter, object? value);

    // #4819: ToJson / ToCleanJson / the FromJson + FromJsonAsync family moved to the db-neutral
    // IStorageSerializer base (they were already Npgsql-free — Stream / DbDataReader / Type).

    /// <summary>
    ///     UTF-8 / buffer-writer counterpart to <see cref="ToCleanJson"/>. Skips the
    ///     intermediate <see cref="string"/> allocation on the patch-emission hot path.
    /// </summary>
    /// <remarks>
    ///     Implementations must produce identical bytes to what
    ///     <c>Encoding.UTF8.GetBytes(ToCleanJson(value))</c> would emit.
    /// </remarks>
    void WriteToCleanJson(IBufferWriter<byte> writer, object? value);

    /// <summary>
    ///     Write the JSON for a document with embedded
    ///     type information. This is used inside the patching API
    ///     to handle polymorphic collections
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToJsonWithTypes(object document);

    /// <summary>
    ///     UTF-8 / buffer-writer counterpart to <see cref="ToJsonWithTypes"/>. Skips the
    ///     intermediate <see cref="string"/> allocation when emitting the polymorphic
    ///     value payload in a patch operation.
    /// </summary>
    /// <remarks>
    ///     Implementations must produce identical bytes to what
    ///     <c>Encoding.UTF8.GetBytes(ToJsonWithTypes(value))</c> would emit.
    /// </remarks>
    void WriteToJsonWithTypes(IBufferWriter<byte> writer, object value);
}

#endregion

// Casing consolidated to Weasel.Core per the dedup audit (marten#4527 / pillar #214);
// aliased in src/Shared/DedupeAliases.cs.

/// <summary>
///     Governs .Net collection serialization
/// </summary>
public enum CollectionStorage
{
    /// <summary>
    ///     Use default serialization for collections according to the serializer
    ///     being used
    /// </summary>
    Default,

    /// <summary>
    ///     Direct the underlying serializer to serialize collections as JSON arrays
    /// </summary>
    AsArray
}

// NonPublicMembersStorage consolidated to Weasel.Core per the dedup audit
// (marten#4527 / pillar #214); aliased in src/Shared/DedupeAliases.cs.
