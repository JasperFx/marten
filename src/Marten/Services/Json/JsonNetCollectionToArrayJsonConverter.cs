using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Marten.Services.Json;

/// <summary>
///     Serialize collection type property to JSON array using a custom Newtonsoft.Json JsonConverter
///     Note that without using custom `JsonConverter`, `Newtonsoft.Json` stores it as $type and $value.
///     Or you may need to resort to `Newtonsoft.Json.TypeNameHandling.None` which has its own side-effects
/// </summary>
public class JsonNetCollectionToArrayJsonConverter: JsonConverter
{
    public static JsonNetCollectionToArrayJsonConverter Instance = new();

    private static readonly List<Type> _types =
        [typeof(ICollection<>), typeof(IList<>), typeof(IReadOnlyCollection<>), typeof(IEnumerable<>)];

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var o = value as IEnumerable;
        serializer.Serialize(writer, o?.Cast<object>().ToArray());
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        return serializer.Deserialize(reader, objectType);
    }

    public override bool CanConvert(Type objectType)
    {
        return _types.Contains(objectType)
               || objectType.IsArray
               || (objectType.IsGenericType && _types.Contains(objectType.GetGenericTypeDefinition()));
    }
}
