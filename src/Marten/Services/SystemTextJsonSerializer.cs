using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services.Json;
using Marten.Util;

namespace Marten.Services
{
    public class SystemTextJsonSerializer: ISerializer
    {
        private EnumStorage _enumStorage = EnumStorage.AsInteger;
        private Casing _casing = Casing.Default;

        private readonly JsonSerializerOptions _clean = new();

        private readonly JsonSerializerOptions _options = new();

        private readonly JsonSerializerOptions _optionsDeserialize = new();

        private readonly JsonSerializerOptions _withTypes = new();

        public SystemTextJsonSerializer()
        {
            _optionsDeserialize.Converters.Add(new SystemObjectNewtonsoftCompatibleConverter());
            _optionsDeserialize.PropertyNamingPolicy =
                _options.PropertyNamingPolicy
                    = _clean.PropertyNamingPolicy
                        = _withTypes.PropertyNamingPolicy = null;
            // _optionsDeserialize.EnableDynamicTypes();
        }

        /// <summary>
        /// Customize the inner System.Text.Json formatter.
        /// </summary>
        /// <param name="configure"></param>
        public void Customize(Action<JsonSerializerOptions> configure)
        {
            configure(_clean);
            configure(_options);
            configure(_optionsDeserialize);
            configure(_withTypes);
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
            using (NoSynchronizationContextScope.Enter())
            {
                return FromJsonAsync<T>(stream).GetAwaiter().GetResult();
            }
        }

        public async Task<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            return await JsonSerializer.DeserializeAsync<T>(await stream.SkipSOHAsync(cancellationToken), _optionsDeserialize, cancellationToken);
        }

        public object FromJson(Type type, Stream stream)
        {
            using (NoSynchronizationContextScope.Enter())
            {
                return FromJsonAsync(type, stream).GetAwaiter().GetResult();
            }
        }

        public async Task<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        {
            return await JsonSerializer.DeserializeAsync(await stream.SkipSOHAsync(cancellationToken), type, _optionsDeserialize, cancellationToken);
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

                var jsonNamingPolicy = _casing switch
                {
                    Casing.CamelCase => JsonNamingPolicy.CamelCase,
                    Casing.SnakeCase => new JsonSnakeCaseNamingPolicy(),
                    _ => null
                };

                _optionsDeserialize.PropertyNamingPolicy =
                    _options.PropertyNamingPolicy
                        = _clean.PropertyNamingPolicy
                            = _withTypes.PropertyNamingPolicy = jsonNamingPolicy;

                _options.Converters.RemoveAll(x => x is JsonStringEnumConverter);
                _optionsDeserialize.Converters.RemoveAll(x => x is JsonStringEnumConverter);
                _clean.Converters.RemoveAll(x => x is JsonStringEnumConverter);
                _withTypes.Converters.RemoveAll(x => x is JsonStringEnumConverter);

                if (_enumStorage == EnumStorage.AsString)
                {
                    var converter = new JsonStringEnumConverter();
                    _options.Converters.Add(converter);
                    _optionsDeserialize.Converters.Add(converter);
                    _clean.Converters.Add(converter);
                    _withTypes.Converters.Add(converter);
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
