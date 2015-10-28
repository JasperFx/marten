using System.IO;
using Newtonsoft.Json;

namespace Marten
{
    public class JsonNetSerializer : ISerializer
    {
        private readonly JsonSerializer _serializer = new JsonSerializer
        {
            TypeNameHandling = TypeNameHandling.Auto
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
    }
}