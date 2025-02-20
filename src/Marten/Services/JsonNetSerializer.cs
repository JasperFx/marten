#nullable enable
using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Services.Json;
using Marten.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
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

    public string ToJsonWithTypes(object document)
    {
        var writer = new StringWriter();

        _withTypes.Value.Serialize(writer, document);

        return writer.ToString();
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
