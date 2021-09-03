# Json Serialization

An absolutely essential ingredient in Marten's persistence strategy is JSON serialization of the document objects. Marten aims to make the
JSON serialization extensible and configurable through the native mechanisms in each JSON serialization library. For the purposes of having
a smooth "getting started" story, Marten comes out of the box with support for a very basic usage of Newtonsoft.Json as the main JSON serializer.

Internally, Marten uses an adapter interface for JSON serialization:

<!-- snippet: sample_ISerializer -->
<a id='snippet-sample_iserializer'></a>
```cs
/// <summary>
/// When selecting data through Linq Select() transforms,
/// should the data elements returned from Postgresql be
/// cast to their raw types or simple strings
/// </summary>
public enum ValueCasting
{
    /// <summary>
    /// Json fields will be returned with their values cast to
    /// the proper type. I.e., {"number": 1}
    /// </summary>
    Strict,

    /// <summary>
    /// Json fields will be returned with their values in simple
    /// string values. I.e., {"number": "1"}
    /// </summary>
    Relaxed
}

public interface ISerializer
{
    /// <summary>
    /// Serialize the document object into a JSON string
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToJson(object document);

    /// <summary>
    /// Deserialize a JSON string stream into an object of type T
    /// </summary>
    T FromJson<T>(Stream stream);

    /// <summary>
    /// Deserialize a JSON string into an object of type T
    /// </summary>
    T FromJson<T>(DbDataReader reader, int index);

    /// <summary>
    /// Deserialize a JSON string stream into an object of type T
    /// </summary>
    ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize a JSON string into an object of type T
    /// </summary>
    ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize a JSON string stream into an object of type T
    /// </summary>
    object FromJson(Type type, Stream stream);

    /// <summary>
    /// Deserialize a JSON string into the supplied Type
    /// </summary>
    object FromJson(Type type, DbDataReader reader, int index);

    /// <summary>
    /// Deserialize a JSON string stream into an object of type T
    /// </summary>
    ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize a JSON string into the supplied Type
    /// </summary>
    ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serialize a document without any extra
    /// type handling metadata
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToCleanJson(object document);

    /// <summary>
    /// Just gotta tell Marten if enum's are stored
    /// as int's or string's in the JSON
    /// </summary>
    EnumStorage EnumStorage { get; }

    /// <summary>
    /// Specify whether properties in the JSON document should use Camel or Pascal casing.
    /// </summary>
    Casing Casing { get; }

    /// <summary>
    /// Write the JSON for a document with embedded
    /// type information. This is used inside the patching API
    /// to handle polymorphic collections
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToJsonWithTypes(object document);

    /// <summary>
    /// Controls how the Linq Select() behavior needs to work in the database
    /// </summary>
    ValueCasting ValueCasting { get; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/ISerializer.cs#L14-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iserializer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To support a new serialization library or customize the JSON serialization options, you can write a new version of `ISerializer` and plug it
into the `DocumentStore` (there's an example of doing that in the section on using Jil).
