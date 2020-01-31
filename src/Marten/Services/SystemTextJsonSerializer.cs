using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baseline;

namespace Marten.Services
{
    public class SystemTextJsonSerializer : ISerializer
    {
        private EnumStorage _enumStorage = EnumStorage.AsInteger;
        private Casing _casing = Casing.Default;

        private readonly JsonSerializerOptions _clean = new JsonSerializerOptions
        {
        };

        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
        };

        private readonly JsonSerializerOptions _withTypes = new JsonSerializerOptions
        {
        };

        /// <summary>
        /// Customize the inner System.Text.Json formatter.
        /// </summary>
        /// <param name="configure"></param>
        public void Customize(Action<JsonSerializerOptions> configure)
        {
            configure(_clean);
            configure(_options);
        }

        public void ToJson(object document, Stream stream)
        {
            using var writer = new Utf8JsonWriter(stream);
            JsonSerializer.Serialize(writer, document, _options);
        }

        public string ToJson(object document)
        {
            return JsonSerializer.Serialize(document, document.GetType(), _options);
        }

        public T FromJson<T>(Stream stream)
        {
            var reader = new Utf8JsonReader();
            return JsonSerializer.Deserialize<T>(ref reader);
        }

        public object FromJson(Type type, Stream stream)
        {
            var reader = new Utf8JsonReader();
            return JsonSerializer.Deserialize(ref reader, type, _options);
        }

        public string ToCleanJson(object document)
        {
            return JsonSerializer.Serialize(document, _clean);
        }

        public string ToJsonWithTypes(object document)
        {
            return JsonSerializer.Serialize(document, _withTypes);
        }

        /// <inheritdoc />
        public EnumStorage EnumStorage
        {
            get => _enumStorage;
            set
            {
                _enumStorage = value;

                if (value == EnumStorage.AsString)
                {
                    JsonNamingPolicy jsonNamingPolicy = null;
                    if (_casing == Casing.CamelCase)
                    {
                        jsonNamingPolicy = JsonNamingPolicy.CamelCase;
                    }

                    var converter = new JsonStringEnumConverter(jsonNamingPolicy);
                    _options.Converters.Add(converter);
                    _clean.Converters.Add(converter);
                }
                else
                {
                    _options.Converters.RemoveAll(x => x is JsonStringEnumConverter);
                    _clean.Converters.RemoveAll(x => x is JsonStringEnumConverter);
                }
            }
        }

        /// <inheritdoc />
        public Casing Casing
        {
            get => _casing;
            set
            {
                _casing = value;
                // ensure we refresh
                EnumStorage = _enumStorage;
            }
        }
    }
}
