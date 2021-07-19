# Serializing with Newtonsoft.Json

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Services/JsonNetSerializer.cs#L40-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_newtonsoft-configuration' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L96-L118' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
You should not override the Newtonsoft.Json `ContractResolver` with `CamelCasePropertyNamesContractResolver` for Json Serialization. Newtonsoft.Json by default respects the casing used in property / field names which is typically PascalCase.
This can be overridden to serialize the names to camelCase and Marten will store the JSON in the database as specified by the Newtonsoft.Json settings. However, Marten uses the property / field names casing for its SQL queries and queries are case sensitive and as such, querying will not work correctly.

Marten actually has to keep two Newtonsoft.Json serializers, with one being a "clean" Json serializer that omits all Type metadata. The need for two serializers is why the customization is done with a nested closure so that the same configuration is always applied to both internal `JsonSerializer's`.
:::

## Enum Storage

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L123-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_enum_storage_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Fields Names Casing

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L138-L148' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_camelcase_casing_serialization' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L153-L163' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_snakecase_casing_serialization' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Collection Storage

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L168-L178' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_snakecase_collectionstorage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Non Public Members Storage

By default `Newtonsoft.Json` only deserializes properties with public setters.

You can allow deserialisation of properties with non-public setters by changing the serialization settings in the `DocumentStore` options.

<!-- snippet: sample_customize_json_net_snakecase_nonpublicmembersstorage_nonpublicsetters -->
<a id='snippet-sample_customize_json_net_snakecase_nonpublicmembersstorage_nonpublicsetters'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("some connection string");

    // Replace the default (only public setters) JsonNetSerializer deserialization settings
    // with allowing to also deserialize using non-public setters
    _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L183-L193' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customize_json_net_snakecase_nonpublicmembersstorage_nonpublicsetters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
