#nullable enable
using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Services.Json;
using Marten.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;

namespace Marten.Services;

/// <summary>
///     Serialization with Newtonsoft.Json
/// </summary>
public class JsonNetSerializer: ISerializer
{
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Create();

    private readonly JsonSerializerSettings _cleanSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        ContractResolver = new JsonNetContractResolver()
    };

    private readonly Lazy<JsonSerializer> _clean;

    private readonly JsonArrayPool<char> _jsonArrayPool;

    #region sample_newtonsoft-configuration

    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,

        // ISO 8601 formatting of DateTime's is mandatory
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
        ContractResolver = new JsonNetContractResolver()
    };

    #endregion

    private readonly Lazy<JsonSerializer> _serializer;

    private readonly JsonSerializerSettings _withTypesSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Objects,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        ContractResolver = new JsonNetContractResolver()
    };

    private readonly Lazy<JsonSerializer> _withTypes;

    private Casing _casing = Casing.Default;
    private CollectionStorage _collectionStorage = CollectionStorage.Default;

    private EnumStorage _enumStorage = EnumStorage.AsInteger;
    private NonPublicMembersStorage _nonPublicMembersStorage;

    public JsonNetSerializer()
    {
        _jsonArrayPool = new JsonArrayPool<char>(_charPool);
        _clean = new(() => JsonSerializer.Create(_cleanSettings));
        _serializer = new(() => JsonSerializer.Create(_serializerSettings));
        _withTypes = new(() => JsonSerializer.Create(_withTypesSettings));
    }

    /// <summary>
    ///     Specify whether collections should be stored as json arrays (without type names)
    /// </summary>
    public CollectionStorage CollectionStorage
    {
        get => _collectionStorage;
        set
        {
            _collectionStorage = value;

            _serializerSettings.ContractResolver =
                new JsonNetContractResolver(Casing, _collectionStorage, NonPublicMembersStorage);
            _cleanSettings.ContractResolver = new JsonNetContractResolver(Casing, _collectionStorage, NonPublicMembersStorage);
        }
    }

    /// <summary>
    ///     Specify whether non public members should be used during deserialization
    /// </summary>
    public NonPublicMembersStorage NonPublicMembersStorage
    {
        get => _nonPublicMembersStorage;
        set
        {
            _nonPublicMembersStorage = value;

            if (_nonPublicMembersStorage.HasFlag(NonPublicMembersStorage.NonPublicDefaultConstructor))
            {
                _serializerSettings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            }

            _serializerSettings.ContractResolver =
                new JsonNetContractResolver(Casing, CollectionStorage, _nonPublicMembersStorage);
            _cleanSettings.ContractResolver = new JsonNetContractResolver(Casing, CollectionStorage, _nonPublicMembersStorage);
        }
    }

    public string ToJson(object? document)
    {
        var writer = new StringWriter();
        ToJson(document, writer);

        return writer.ToString();
    }

    public void WriteTo(IBufferWriter<byte> writer, object? value)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        // JsonTextWriter writes chars to a TextWriter — wrap the IBufferWriter<byte> in a
        // BufferWriterStream and let a UTF-8 StreamWriter do the char→byte transcoding.
        // The leaveOpen flag on StreamWriter doesn't matter here because the surrounding
        // stream is a no-op on Dispose; the explicit Flush calls below guarantee bytes
        // are pushed to the IBufferWriter before WriteTo returns.
        using var stream = new BufferWriterStream(writer);
        // Skip the BOM by passing a UTF8Encoding(false) — we don't want a 0xEF 0xBB 0xBF
        // prefix in the middle of our JSON byte stream.
        using var streamWriter = new StreamWriter(stream, _utf8NoBom, bufferSize: 1024, leaveOpen: true);
        using var jsonWriter = new JsonTextWriter(streamWriter)
        {
            ArrayPool = _jsonArrayPool, CloseOutput = false, AutoCompleteOnClose = false
        };

        _serializer.Value.Serialize(jsonWriter, value);
        jsonWriter.Flush();
        streamWriter.Flush();
    }

    public void WriteToParameter(NpgsqlParameter parameter, object? value)
    {
        if (parameter is null) throw new ArgumentNullException(nameof(parameter));

        parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        // Serialize into a pooled buffer to avoid the intermediate StringWriter+char[]
        // path that ToJson takes, then snapshot a sized byte[] for Npgsql to retain
        // (the pooled buffer is returned on Dispose at end of method scope).
        using var buffer = new PooledByteBufferWriter();
        WriteTo(buffer, value);
        parameter.Value = buffer.ToSizedArray();
    }

    // UTF8Encoding(false) suppresses the BOM. Cached on the type because StreamWriter
    // copies the encoding's flags but doesn't keep a reference.
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public T FromJson<T>(Stream stream)
    {
        using var jsonReader = GetJsonTextReader(stream);

        return _serializer.Value.Deserialize<T>(jsonReader)!;
    }

    public T FromJson<T>(DbDataReader reader, int index)
    {
        using var textReader = reader.GetTextReader(index);
        using var jsonReader = GetJsonTextReader(textReader);

        return _serializer.Value.Deserialize<T>(jsonReader)!;
    }

    public ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return new ValueTask<T>(FromJson<T>(stream));
    }

    public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
    {
        return new ValueTask<T>(FromJson<T>(reader, index));
    }

    public object FromJson(Type type, Stream stream)
    {
        using var jsonReader = GetJsonTextReader(stream);

        return _serializer.Value.Deserialize(jsonReader, type)!;
    }

    public object FromJson(Type type, DbDataReader reader, int index)
    {
        using var textReader = reader.GetTextReader(index);
        using var jsonReader = GetJsonTextReader(textReader);

        return _serializer.Value.Deserialize(jsonReader, type)!;
    }

    public ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
    {
        return new ValueTask<object>(FromJson(type, stream));
    }

    public ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<object>(FromJson(type, reader, index));
    }

    public string ToCleanJson(object? document)
    {
        var writer = new StringWriter();

        _clean.Value.Serialize(writer, document);

        return writer.ToString();
    }

    public void WriteToCleanJson(IBufferWriter<byte> writer, object? value)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        // Mirror WriteTo's transport but use _clean instead of _serializer so the
        // emitted bytes match ToCleanJson byte-for-byte.
        using var stream = new BufferWriterStream(writer);
        using var streamWriter = new StreamWriter(stream, _utf8NoBom, bufferSize: 1024, leaveOpen: true);
        using var jsonWriter = new JsonTextWriter(streamWriter)
        {
            ArrayPool = _jsonArrayPool, CloseOutput = false, AutoCompleteOnClose = false
        };

        _clean.Value.Serialize(jsonWriter, value);
        jsonWriter.Flush();
        streamWriter.Flush();
    }

    public string ToJsonWithTypes(object document)
    {
        var writer = new StringWriter();

        _withTypes.Value.Serialize(writer, document);

        return writer.ToString();
    }

    public void WriteToJsonWithTypes(IBufferWriter<byte> writer, object value)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (value is null) throw new ArgumentNullException(nameof(value));

        // Mirror WriteTo's transport but use _withTypes so the emitted bytes match
        // ToJsonWithTypes byte-for-byte (TypeNameHandling.Objects $type metadata).
        using var stream = new BufferWriterStream(writer);
        using var streamWriter = new StreamWriter(stream, _utf8NoBom, bufferSize: 1024, leaveOpen: true);
        using var jsonWriter = new JsonTextWriter(streamWriter)
        {
            ArrayPool = _jsonArrayPool, CloseOutput = false, AutoCompleteOnClose = false
        };

        _withTypes.Value.Serialize(jsonWriter, value);
        jsonWriter.Flush();
        streamWriter.Flush();
    }

    /// <summary>
    ///     Specify whether .Net Enum values should be stored as integers or strings
    ///     within the Json document. Default is AsInteger.
    /// </summary>
    public EnumStorage EnumStorage
    {
        get => _enumStorage;
        set
        {
            _enumStorage = value;

            if (value == EnumStorage.AsString)
            {
                var converter = new StringEnumConverter();
                _serializerSettings.Converters.Add(converter);
                _cleanSettings.Converters.Add(converter);
            }
            else
            {
                _serializerSettings.Converters.RemoveAll(x => x is StringEnumConverter);
                _cleanSettings.Converters.RemoveAll(x => x is StringEnumConverter);
            }
        }
    }

    /// <summary>
    ///     Specify whether properties in the JSON document should use Camel or Pascal casing.
    /// </summary>
    public Casing Casing
    {
        get => _casing;
        set
        {
            _casing = value;

            _serializerSettings.ContractResolver =
                new JsonNetContractResolver(_casing, CollectionStorage, NonPublicMembersStorage);
            _cleanSettings.ContractResolver =
                new JsonNetContractResolver(_casing, CollectionStorage, NonPublicMembersStorage);
        }
    }

    public ValueCasting ValueCasting => ValueCasting.Relaxed;

    /// <summary>
    /// Configure the <see cref="JsonSerializerSettings"/> of the Newtonsoft serializer.
    /// </summary>
    /// <param name="configure"></param>
    public void Configure(Action<JsonSerializerSettings> configure)
    {
        configure(_cleanSettings);
        configure(_serializerSettings);
        configure(_withTypesSettings);

        _cleanSettings.TypeNameHandling = TypeNameHandling.None;
        _withTypesSettings.TypeNameHandling = TypeNameHandling.Objects;
    }

    private void ToJson(object? document, TextWriter writer)
    {
        using var jsonWriter = new JsonTextWriter(writer)
        {
            ArrayPool = _jsonArrayPool, CloseOutput = false, AutoCompleteOnClose = false
        };

        _serializer.Value.Serialize(jsonWriter, document);

        writer.Flush();
    }

    public JObject JObjectFromJson(Stream stream)
    {
        using var jsonReader = GetJsonTextReader(stream);

        return JObject.Load(jsonReader);
    }

    public JObject JObjectFromJson(DbDataReader reader, int index)
    {
        using var textReader = reader.GetTextReader(index);
        using var jsonReader = GetJsonTextReader(textReader);
        return JObject.Load(jsonReader);
    }

    private JsonTextReader GetJsonTextReader(Stream stream)
    {
        return new(stream.GetStreamReader()) { ArrayPool = _jsonArrayPool, CloseInput = false };
    }

    private JsonTextReader GetJsonTextReader(TextReader textReader)
    {
        return new(textReader) { ArrayPool = _jsonArrayPool, CloseInput = false };
    }
}
