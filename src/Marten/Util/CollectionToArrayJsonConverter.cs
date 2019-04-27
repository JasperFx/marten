using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Marten.Util
{
    /// <summary>
    /// Serialize collection type property to JSON array using a custom Newtonsoft.Json JsonConverter
    /// Note that without using custom `JsonConverter`, `Newtonsoft.Json` stores it as $type and $value.
    /// Or you may need to resort to `Newtonsoft.Json.TypeNameHandling.None` which has its own side-effects   
    /// </summary>
    /// <typeparam name="T">Type of the collection</typeparam>
    public class CollectionToArrayJsonConverter<T> : JsonConverter
    {
        private readonly List<Type> _types = new List<Type>
        {
            typeof(ICollection<T>),
            typeof(IReadOnlyCollection<T>),
            typeof(IEnumerable<T>),
            typeof(T[])
        };
        
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var o = value as IEnumerable<T>;
            serializer.Serialize(writer, o?.ToArray());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return serializer.Deserialize<List<T>>(reader);
        }

        public override bool CanConvert(Type objectType)
        {
            return _types.Contains(objectType);
        }
    }
}