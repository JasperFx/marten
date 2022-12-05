# Json Serialization

::: tip
Newtonsoft.Json is still the default JSON serializer in Marten for backwards compatibility
with previous Marten versions and because it is the most battle-hardened JSON serializer
in the .Net space that "just works." Other, more performant serializers can also
be used with Marten.
:::

An absolutely essential ingredient in Marten's persistence strategy is JSON serialization of the document objects. Marten aims to make the
JSON serialization extensible and configurable through the native mechanisms in each JSON serialization library. For the purposes of having
a smooth "getting started" story, Marten comes out of the box with support for a very basic usage of Newtonsoft.Json as the main JSON serializer.

Internally, Marten uses an adapter interface for JSON serialization:

<!-- snippet: sample_ISerializer -->
<a id='snippet-sample_iserializer'></a>
```cs
/// <summary>
///     When selecting data through Linq Select() transforms,
///     should the data elements returned from Postgresql be
///     cast to their raw types or simple strings
/// </summary>
public enum ValueCasting
{
    /// <summary>
    ///     Json fields will be returned with their values cast to
    ///     the proper type. I.e., {"number": 1}
    /// </summary>
    Strict,

    /// <summary>
    ///     Json fields will be returned with their values in simple
    ///     string values. I.e., {"number": "1"}
    /// </summary>
    Relaxed
}

public interface ISerializer
{
    /// <summary>
    ///     Just gotta tell Marten if enum's are stored
    ///     as int's or string's in the JSON
    /// </summary>
    EnumStorage EnumStorage { get; }

    /// <summary>
    ///     Specify whether properties in the JSON document should use Camel or Pascal casing.
    /// </summary>
    Casing Casing { get; }

    /// <summary>
    ///     Controls how the Linq Select() behavior needs to work in the database
    /// </summary>
    ValueCasting ValueCasting { get; }

    /// <summary>
    ///     Serialize the document object into a JSON string
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToJson(object? document);

    /// <summary>
    ///     Deserialize a JSON string stream into an object of type T
    /// </summary>
    T FromJson<T>(Stream stream);

    /// <summary>
    ///     Deserialize a JSON string into an object of type T
    /// </summary>
    T FromJson<T>(DbDataReader reader, int index);

    /// <summary>
    ///     Deserialize a JSON string stream into an object of type T
    /// </summary>
    ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deserialize a JSON string into an object of type T
    /// </summary>
    ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deserialize a JSON string stream into an object of type T
    /// </summary>
    object FromJson(Type type, Stream stream);

    /// <summary>
    ///     Deserialize a JSON string into the supplied Type
    /// </summary>
    object FromJson(Type type, DbDataReader reader, int index);

    /// <summary>
    ///     Deserialize a JSON string stream into an object of type T
    /// </summary>
    ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deserialize a JSON string into the supplied Type
    /// </summary>
    ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Serialize a document without any extra
    ///     type handling metadata
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToCleanJson(object? document);

    /// <summary>
    ///     Write the JSON for a document with embedded
    ///     type information. This is used inside the patching API
    ///     to handle polymorphic collections
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    string ToJsonWithTypes(object document);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/ISerializer.cs#L11-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iserializer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To support a new serialization library or customize the JSON serialization options, you can write a new version of `ISerializer` and plug it
into the `DocumentStore` (there's an example of doing that in the section on using Jil).

::: tip
Regardless of which JSON serializer you use, make sure to set the `Casing` property
on the Marten `ISerializer` interface instead of directly overriding the member
naming on the underlying JSON serializer. The Linq querying support needs this information
in order to create the correct SQL queries within JSON bodies.
:::

## Serializing with Newtonsoft.Json

The default JSON serialization strategy inside of Marten uses [Newtonsoft.Json](http://www.newtonsoft.com/json). We have standardized on Newtonsoft.Json
because of its flexibility and ability to handle polymorphism within child collections. Marten also uses Newtonsoft.Json internally to do JSON diff's for the automatic dirty checking option.

Out of the box, Marten uses this configuration for Newtonsoft.Json:

<!-- snippet: sample_newtonsoft-configuration -->
<a id='snippet-sample_newtonsoft-configuration'></a>
```cs
private readonly JsonSerializer _serializer = new()
{
    TypeNameHandling = TypeNameHandling.Auto,

    // ISO 8601 formatting of DateTime's is mandatory
    DateFormatHandling = DateFormatHandling.IsoDateFormat,
    MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
    ContractResolver = new JsonNetContractResolver()
};
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Services/JsonNetSerializer.cs#L34-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_newtonsoft-configuration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To customize the Newtonsoft.Json serialization, you need to explicitly supply an instance of Marten's `JsonNetSerializer` as shown below:

<!-- snippet: sample_customize_json_net_serialization -->
<a id='snippet-sample_customize_json_net_serialization'></a>
```cs
var serializer = new Marten.Services.JsonNetSerializer();

// To change the enum storage policy to store Enum's as strings:
serializer.EnumStorage = EnumStorage.AsString;

// All other customizations:
serializer.Customize(_ =>
{
    // Code directly against a Newtonsoft.Json JsonSerializer
    _.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
    _.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
});

var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default JsonNetSerializer with the one we configured
    // above
    _.Serializer(serializer);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L89-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
You should not override the Newtonsoft.Json `ContractResolver` with `CamelCasePropertyNamesContractResolver` for Json Serialization. Newtonsoft.Json by default respects the casing used in property / field names which is typically PascalCase.
This can be overridden to serialize the names to camelCase and Marten will store the JSON in the database as specified by the Newtonsoft.Json settings. However, Marten uses the property / field names casing for its SQL queries and queries are case sensitive and as such, querying will not work correctly.

Marten actually has to keep two Newtonsoft.Json serializers, with one being a "clean" Json serializer that omits all Type metadata. The need for two serializers is why the customization is done with a nested closure so that the same configuration is always applied to both internal `JsonSerializer's`.
:::

### Enum Storage

Marten allows how enum values are being stored. By default, they are stored as integers but it is possible to change that to storing them as strings.

To do that you need to change the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_enum_storage_serialization -->
<a id='snippet-sample_customize_json_net_enum_storage_serialization'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default JsonNetSerializer default enum storage
    // with storing them as string
    _.UseDefaultSerialization(enumStorage: EnumStorage.AsString);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L116-L126' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_enum_storage_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Fields Names Casing

Marten by default stores field names "as they are" (C# naming convention is PascalCase for public properties).

You can have them also automatically formatted to:

- `CamelCase`,
- `snake_case`

by changing the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_camelcase_casing_serialization -->
<a id='snippet-sample_customize_json_net_camelcase_casing_serialization'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default (as is) JsonNetSerializer field names casing
    // with camelCase formatting
    _.UseDefaultSerialization(casing: Casing.CamelCase);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L131-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_camelcase_casing_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_customize_json_net_snakecase_casing_serialization -->
<a id='snippet-sample_customize_json_net_snakecase_casing_serialization'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default (as is) JsonNetSerializer field names casing
    // with snake_case formatting
    _.UseDefaultSerialization(casing: Casing.SnakeCase);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L146-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_snakecase_casing_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Collection Storage

Marten by default stores the collections as strongly typed (so with $type and $value). Because of that and current `MartenQueryable` limitations, it might result in not properly resolved nested collections queries.

Changing the collection storage to `AsArray` using a custom `JsonConverter` will store it as regular JSON array for the following:

- `ICollection<>`,
- `IList<>`,
- `IReadOnlyCollection<>`,
- `IEnumerable<>`.

That improves the nested collections queries handling.

To do that you need to change the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_snakecase_collectionstorage -->
<a id='snippet-sample_customize_json_net_snakecase_collectionstorage'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default (strongly typed) JsonNetSerializer collection storage
    // with JSON array formatting
    _.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L161-L171' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_snakecase_collectionstorage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Non Public Members Storage

By default `Newtonsoft.Json` only deserializes properties with public setters.

You can allow deserialization of properties with non-public setters by changing the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_nonpublicsetters -->
<a id='snippet-sample_customize_json_net_nonpublicsetters'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default (only public setters) JsonNetSerializer deserialization settings
    // with allowing to also deserialize using non-public setters
    _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L176-L186' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_nonpublicsetters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use other options of `NonPublicMembersStorage`:

- `NonPublicDefaultConstructor` - allows deserialization using non-public default constructor,
- `NonPublicConstructor` - allows deserialization using any constructor. Construction resolution uses the following precedence:
  1. Constructor with `JsonConstructor` attribute.
  2. Constructor with the biggest parameters' count.
  3. If two constructors have the same parameters' count, use public or take the first one.
  4. Use default constructor.
- `All` - Use both properties with non-public setters and non-public constructors.

When using `System.Text.Json` the only support for private properties is to mark the field using [\[JsonInclude\]](https://docs.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonincludeattribute?view=net-6.0) attribute.

Alternatively if you want immutability you can mark the setter as `init` like so:

```cs
public class User
{
    public int Id { get; init; }
}
```

## Serialization with System.Text.Json

::: tip
The Marten team only recommends using the System.Text.Json serializer in new systems.
The behavior is different enough from Newtonsoft.Json that conversions of existing Marten
applications to System.Text.Json should be done with quite a bit of caution and testing.
:::

New in Marten V4 is support for the [System.Text.Json](https://docs.microsoft.com/en-us/dotnet/api/system.text.json?view=net-5.0) serializer.

<!-- snippet: sample_using_STJ_serialization -->
<a id='snippet-sample_using_stj_serialization'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Opt into System.Text.Json serialization
    opts.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);

    // Optionally configure the serializer directly
    opts.Serializer(new SystemTextJsonSerializer
    {
        // Optionally override the enum storage
        EnumStorage = EnumStorage.AsString,

        // Optionally override the member casing
        Casing = Casing.CamelCase
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/UsingSystemTextJsonSerializer.cs#L11-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_stj_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Serializing with Jil

Marten has also been tested using the [Jil library](https://github.com/kevin-montrose/Jil) for JSON serialization. While Jil is not as
flexible as Newtonsoft.Json and might be missing support for some scenarios you may encounter, it is very clearly faster than Newtonsoft.Json.

To use Jil inside of Marten, add a class to your system like this one that implements the `ISerializer` interface:

<!-- snippet: sample_JilSerializer -->
<a id='snippet-sample_jilserializer'></a>
```cs
public class JilSerializer : ISerializer
{
    private readonly Options _options
        = new(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

    public ValueCasting ValueCasting { get; } = ValueCasting.Strict;

    public string ToJson(object? document)
    {
        return JSON.Serialize(document, _options);
    }

    public T FromJson<T>(Stream stream)
    {
        return JSON.Deserialize<T>(stream.GetStreamReader(), _options);
    }

    public T FromJson<T>(DbDataReader reader, int index)
    {
        var stream = reader.GetStream(index);
        return FromJson<T>(stream);
    }

    public ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return new(FromJson<T>(stream));
    }

    public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
    {
        return new (FromJson<T>(reader, index));
    }

    public object FromJson(Type type, Stream stream)
    {
        return JSON.Deserialize(stream.GetStreamReader(), type, _options);
    }

    public object FromJson(Type type, DbDataReader reader, int index)
    {
        var stream = reader.GetStream(index);
        return FromJson(type, stream);
    }

    public ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
    {
        return new (FromJson(type, stream));
    }

    public ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default)
    {
        return new (FromJson(type, reader, index));
    }

    public string ToCleanJson(object? document)
    {
        return ToJson(document);
    }

    public EnumStorage EnumStorage => EnumStorage.AsString;
    public Casing Casing => Casing.Default;
    public string ToJsonWithTypes(object document)
    {
        throw new NotSupportedException();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/performance_tuning.cs#L15-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_jilserializer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, replace the default `ISerializer` when you bootstrap your `DocumentStore` as in this example below:

<!-- snippet: sample_replacing_serializer_with_jil -->
<a id='snippet-sample_replacing_serializer_with_jil'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("the connection string");

    // Replace the ISerializer w/ the TestsSerializer
    _.Serializer<TestsSerializer>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/performance_tuning.cs#L93-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_replacing_serializer_with_jil' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

See [Optimizing for Performance in Marten](http://jeremydmiller.com/2015/11/09/optimizing-for-performance-in-marten/)
and [Optimizing Marten Part 2](http://jeremydmiller.com/2015/11/30/optimizing-marten-part-2/) for some performance comparisons
of using Jil versus Newtonsoft.Json for serialization within Marten operations.
