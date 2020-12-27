using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Baseline;
using Marten.Services.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StreamExtensions = Marten.Util.StreamExtensions;

namespace Marten.Services
{
    public class JsonNetSerializer: ISerializer
    {
        private readonly ArrayPool<char> _charPool = ArrayPool<char>.Create();
        private readonly JsonArrayPool<char> _jsonArrayPool;

        private readonly JsonSerializer _clean = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.None,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            ContractResolver = new JsonNetContractResolver()
        };

        private readonly JsonSerializer _withTypes = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Objects,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            ContractResolver = new JsonNetContractResolver()
        };

        // SAMPLE: newtonsoft-configuration
        private readonly JsonSerializer _serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Auto,

            // ISO 8601 formatting of DateTime's is mandatory
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
            ContractResolver = new JsonNetContractResolver()
        };

        // ENDSAMPLE

        public JsonNetSerializer()
        {
            _jsonArrayPool = new JsonArrayPool<char>(_charPool);
        }

        /// <summary>
        /// Customize the inner Newtonsoft formatter.
        /// </summary>
        /// <param name="configure"></param>
        public void Customize(Action<JsonSerializer> configure)
        {
            configure(_clean);
            configure(_serializer);
            configure(_withTypes);

            _clean.TypeNameHandling = TypeNameHandling.None;
            _withTypes.TypeNameHandling = TypeNameHandling.Objects;
        }

        public void ToJson(object document, Stream stream)
        {
            using var jsonWriter = new JsonTextWriter(new StreamWriter(stream))
            {
                ArrayPool = _jsonArrayPool,
                CloseOutput = false,
                AutoCompleteOnClose = false
            };

            _serializer.Serialize(jsonWriter, document);

            jsonWriter.Flush();
        }

        public string ToJson(object document)
        {
            using var stream = new MemoryStream();
            ToJson(document, stream);

            stream.Seek(0, SeekOrigin.Begin);
            stream.Position = 0;
            var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public T FromJson<T>(Stream stream)
        {
            using var jsonReader = new JsonTextReader(StreamExtensions.GetStreamReader(stream))
            {
                ArrayPool = _jsonArrayPool,
                CloseInput = false,
            };

            return _serializer.Deserialize<T>(jsonReader);
        }

        public Task<T> FromJsonAsync<T>(Stream stream)
        {
            return Task.FromResult(FromJson<T>(stream));
        }

        public object FromJson(Type type, Stream stream)
        {
            using var jsonReader = new JsonTextReader(StreamExtensions.GetStreamReader(stream))
            {
                ArrayPool = _jsonArrayPool,
                CloseInput = false
            };

            return _serializer.Deserialize(jsonReader, type);
        }

        public Task<object> FromJsonAsync(Type type, Stream stream)
        {
            return Task.FromResult(FromJson(type, stream));
        }

        public string ToCleanJson(object document)
        {
            var writer = new StringWriter();

            _clean.Serialize(writer, document);

            return writer.ToString();
        }

        public string ToJsonWithTypes(object document)
        {
            var writer = new StringWriter();

            _withTypes.Serialize(writer, document);

            return writer.ToString();
        }

        private EnumStorage _enumStorage = EnumStorage.AsInteger;
        private Casing _casing = Casing.Default;
        private CollectionStorage _collectionStorage = CollectionStorage.Default;
        private NonPublicMembersStorage _nonPublicMembersStorage;

        /// <summary>
        /// Specify whether .Net Enum values should be stored as integers or strings
        /// within the Json document. Default is AsInteger.
        /// </summary>
        public EnumStorage EnumStorage
        {
            get
            {
                return _enumStorage;
            }
            set
            {
                _enumStorage = value;

                if (value == EnumStorage.AsString)
                {
                    var converter = new StringEnumConverter();
                    _serializer.Converters.Add(converter);
                    _clean.Converters.Add(converter);
                }
                else
                {
                    _serializer.Converters.RemoveAll(x => x is StringEnumConverter);
                    _clean.Converters.RemoveAll(x => x is StringEnumConverter);
                }
            }
        }

        /// <summary>
        /// Specify whether properties in the JSON document should use Camel or Pascal casing.
        /// </summary>
        public Casing Casing
        {
            get
            {
                return _casing;
            }
            set
            {
                _casing = value;

                _serializer.ContractResolver = new JsonNetContractResolver(_casing, CollectionStorage, NonPublicMembersStorage);
                _clean.ContractResolver = new JsonNetContractResolver(_casing, CollectionStorage, NonPublicMembersStorage);
            }
        }

        /// <summary>
        /// Specify whether collections should be stored as json arrays (without type names)
        /// </summary>
        public CollectionStorage CollectionStorage
        {
            get
            {
                return _collectionStorage;
            }
            set
            {
                _collectionStorage = value;

                _serializer.ContractResolver = new JsonNetContractResolver(Casing, _collectionStorage, NonPublicMembersStorage);
                _clean.ContractResolver = new JsonNetContractResolver(Casing, _collectionStorage, NonPublicMembersStorage);
            }
        }

        /// <summary>
        /// Specify whether non public members should be used during deserialization
        /// </summary>
        public NonPublicMembersStorage NonPublicMembersStorage
        {
            get
            {
                return _nonPublicMembersStorage;
            }
            set
            {
                _nonPublicMembersStorage = value;

                if (_nonPublicMembersStorage.HasFlag(NonPublicMembersStorage.NonPublicDefaultConstructor))
                {
                    _serializer.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
                }
                _serializer.ContractResolver = new JsonNetContractResolver(Casing, CollectionStorage, _nonPublicMembersStorage);
                _clean.ContractResolver = new JsonNetContractResolver(Casing, CollectionStorage, _nonPublicMembersStorage);
            }
        }
    }
}
