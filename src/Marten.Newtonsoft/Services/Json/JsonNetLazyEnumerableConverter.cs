using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Marten.Services.Json;

/// <summary>
///     Writes lazy LINQ sequences as plain JSON arrays.
/// </summary>
/// <remarks>
///     <para>
///     Marten's Newtonsoft settings use <see cref="TypeNameHandling.Auto"/>, which records
///     <c>$type</c> whenever a value's runtime type differs from its declared type. LINQ operators
///     return private iterator classes, so assigning one to a persisted member -- e.g.
///     <c>IEnumerable&lt;T&gt; Codes { get; set; }</c> set from <c>.Where(...).Append(...)</c> --
///     used to persist <c>$type</c> as something like
///     <c>System.Linq.Enumerable+AppendPrepend1Iterator`1[[T]]</c>.
///     </para>
///     <para>
///     Those types have no usable constructor, so the write succeeded and every later read of the
///     document threw "Cannot create and populate list type". The stored payload was otherwise
///     fine -- only the recorded type was unusable. See #5076.
///     </para>
///     <para>
///     This converter only claims types that could never have been deserialized in the first
///     place, so it turns a permanently unreadable document into a readable array and leaves
///     every reconstructible collection (arrays, <c>List&lt;T&gt;</c>, user types) exactly as it
///     was. Collection storage as a whole is a separate, opt-in concern -- see
///     <see cref="JsonNetCollectionToArrayJsonConverter"/> and <c>CollectionStorage.AsArray</c>.
///     </para>
/// </remarks>
public sealed class JsonNetLazyEnumerableConverter: JsonConverter
{
    public static readonly JsonNetLazyEnumerableConverter Instance = new();

    /// <summary>
    ///     Write only. These types never appear as a declared member type, so there is nothing to read.
    /// </summary>
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
    {
        if (objectType == null || !typeof(IEnumerable).IsAssignableFrom(objectType))
        {
            return false;
        }

        // Restricted to the framework's own non-public iterators. A user's internal collection
        // type is left alone -- it may well be reconstructible, and suppressing its $type could
        // break a legitimate polymorphic member.
        if (objectType.Namespace is not "System.Linq")
        {
            return false;
        }

        return !objectType.IsPublic && !objectType.IsNestedPublic;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        var items = ((IEnumerable)value).Cast<object>().ToArray();

        // Materialise into an array of the element type rather than object[], so the element
        // type matches what is written and Auto does not stamp $type onto each element.
        var elementType = elementTypeOf(value.GetType()) ?? typeof(object);
        var typed = Array.CreateInstance(elementType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            typed.SetValue(items[i], i);
        }

        serializer.Serialize(writer, typed, typed.GetType());
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer) =>
        throw new NotSupportedException(
            $"{nameof(JsonNetLazyEnumerableConverter)} is write-only; {objectType} is never a declared member type.");

    private static Type? elementTypeOf(Type type) =>
        type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
}
