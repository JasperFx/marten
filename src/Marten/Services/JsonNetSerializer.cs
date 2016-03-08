using System;
using System.IO;
using Newtonsoft.Json;

namespace Marten.Services
{
    public class JsonNetSerializer : ISerializer
    {
        private readonly JsonSerializer _clean = new JsonSerializer
        {
            TypeNameHandling =  TypeNameHandling.None,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };

        private readonly JsonSerializer _serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Auto,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead
        };

        public string ToJson(object document)
        {
            var writer = new StringWriter();
            _serializer.Serialize(writer, document);

            return writer.ToString();
        }

        public T FromJson<T>(string json)
        {
            return _serializer.Deserialize<T>(new JsonTextReader(new StringReader(json)));
        }

        public T FromJson<T>(Stream stream)
        {
            return _serializer.Deserialize<T>(new JsonTextReader(new StreamReader(stream)));
        }

        public object FromJson(Type type, string json)
        {
            return _serializer.Deserialize(new JsonTextReader(new StringReader(json)), type);
        }

        public string ToCleanJson(object document)
        {
            var writer = new StringWriter();
            _clean.Serialize(writer, document);

            return writer.ToString();
        }
    }
}