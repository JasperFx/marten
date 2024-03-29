# Json Serialization

An absolutely essential ingredient in Marten's persistence strategy is JSON serialization of the document objects. Marten aims to make the
JSON serialization extensible and configurable through the native mechanisms in each JSON serialization library. 

## Serializer Choice

Marten ships with implementations for both Newtonsoft.Json & System.Text.Json. Newtonsoft.Json is enabled by default in Marten for backwards compatibility
with previous Marten versions and because it handles some unique edge-cases that System.Text.Json might not. 

That being said, if you're working on a new application
we recommend enabling System.Text.Json for improved performance and serializer alignment with ASP.NET Core & Wolverine defaults.

Configuration for both serializers hang off the `DocumentStore`s `UseNewtonsoftForSerialization` and `UseSystemTextJsonForSerialization` extensions respectively:

<!-- snippet: sample_customize_serializer -->
<a id='snippet-sample_customize_serializer'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Newtonsoft - Enabled by default
    _.UseNewtonsoftForSerialization(); // [!code ++]

    // System.Text.Json - Opt in
    _.UseSystemTextJsonForSerialization(); // [!code ++]
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L83-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_serializer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Fields Names Casing

By default, Marten stores field names "as they are" (C# naming convention is PascalCase for public properties).

You can have them also automatically formatted to:

- `CamelCase`,
- `snake_case`

by changing the relevant serializer settings:

<!-- snippet: sample_customize_json_camelcase_casing_serialization -->
<a id='snippet-sample_customize_json_camelcase_casing_serialization'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Newtonsoft // [!code focus:5]
    _.UseNewtonsoftForSerialization(casing: Casing.CamelCase);

    // STJ
    _.UseSystemTextJsonForSerialization(casing: Casing.CamelCase);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L139-L149' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_camelcase_casing_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Non Public Members Storage

By default `Newtonsoft.Json` only deserializes properties with public setters.

You can allow deserialization of properties with non-public setters by changing the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_nonpublicsetters -->
<a id='snippet-sample_customize_json_net_nonpublicsetters'></a>
```cs
var store = DocumentStore.For(_ =>
{
     // Allow the JsonNetSerializer to also deserialize using non-public setters // [!code focus:2]
    _.UseNewtonsoftForSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L167-L174' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_nonpublicsetters' title='Start of snippet'>anchor</a></sup>
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

## Custom Configuration

Marten also allows you to completely override all serializer settings via the last configuration parameter:

<!-- snippet: sample_customize_json_advanced -->
<a id='snippet-sample_customize_json_advanced'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.UseNewtonsoftForSerialization( // [!code focus:14]
        enumStorage: EnumStorage.AsString,
        configure: settings =>
        {
            settings.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
        });

    _.UseSystemTextJsonForSerialization(
        enumStorage: EnumStorage.AsString,
        configure: settings =>
        {
            settings.MaxDepth = 100;
        });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L179-L197' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_advanced' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning WARNING
You should not override the Newtonsoft.Json `ContractResolver` with `CamelCasePropertyNamesContractResolver` for Json Serialization. Newtonsoft.Json by default respects the casing used in property / field names which is typically PascalCase.
This can be overridden to serialize the names to camelCase and Marten will store the JSON in the database as specified by the Newtonsoft.Json settings. However, Marten uses the property / field names casing for its SQL queries and queries are case sensitive and as such, querying will not work correctly.

Marten actually has to keep two Newtonsoft.Json serializers, with one being a "clean" Json serializer that omits all Type metadata. The need for two serializers is why the customization is done with a nested closure so that the same configuration is always applied to both internal `JsonSerializer's`.
:::

## External Configuration

You might prefer to configure the serializer seperately from the document store configuration and can do so via passing the serializer instance to the `Serializer` method.

An example of configuring Marten's `JsonNetSerializer` is shown below:

<!-- snippet: sample_customize_json_net_serialization -->
<a id='snippet-sample_customize_json_net_serialization'></a>
```cs
var serializer = new Marten.Services.JsonNetSerializer();

// To change the enum storage policy to store Enum's as strings:
serializer.EnumStorage = EnumStorage.AsString;

// All other customizations:
serializer.Configure(_ =>
{
    // Code directly against a Newtonsoft.Json JsonSerializer
    _.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
    _.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
});

var store = DocumentStore.For(_ =>
{
    // Replace the default JsonNetSerializer with the one we configured
    // above
    _.Serializer(serializer);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L99-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Integrating a Custom serializer

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
into the `DocumentStore`. An example of integrating with the `Jil` serializer is below.

::: tip
Regardless of which JSON serializer you use, make sure to set the `Casing` property
on the Marten `ISerializer` interface instead of directly overriding the member
naming on the underlying JSON serializer. The Linq querying support needs this information
in order to create the correct SQL queries within JSON bodies.
:::

::: warning
The code below is provided as an example only, `Jil` is no longer maintained
and should not be used in your application.
:::

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/JilSerializer.cs#L15-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_jilserializer' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/JilSerializer.cs#L93-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_replacing_serializer_with_jil' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
