# Customizing Document Storage

::: warning
To enable Marten's built in schema comparison and data migration tools to work properly, all indexes must
start with the "mt_" prefix. This limitation may be removed in the future, but for now, Marten will throw
an exception if you leave off the required prefix in index definitions.
:::

While you can certainly write your own [DDL](https://en.wikipedia.org/wiki/Data_definition_language)
and SQL queries for optimizing data fetching, Marten gives you a couple options for speeding up queries --
which all come at the cost of slower inserts because it's an imperfect world. Marten supports the ability to configure:

* Indexes on the JSONB data field itself
* Duplicate properties into separate database fields with a matching index for optimized querying
* Choose how Postgresql will search within JSONB documents
* DDL generation rules
* How documents will be deleted

My own personal bias is to avoid adding persistence concerns directly to the document types, but other developers
will prefer to use either attributes or the new embedded configuration option with the thinking that it's
better to keep the persistence configuration on the document type itself for easier traceability. Either way,
Marten has you covered with the various configuration options shown here.

## Postgres Limits on Naming

Postgresql out of the box has a limitation on the length of database object names to 64. This can be overridden in a
Postgresql database by [setting the NAMEDATALEN property](https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS).

This can unfortunately have a negative impact on Marten's ability to detect changes to the schema configuration when Postgresql quietly
truncates the name of database objects. To guard against this, Marten will now warn you if a schema name exceeds the `NAMEDATALEN` value,
but you do need to tell Marten about any non-default length limit like so:

<!-- snippet: sample_setting-name-data-length -->
<a id='snippet-sample_setting-name-data-length'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // If you have overridden NAMEDATALEN in your
    // Postgresql database to 100
    _.NameDataLength = 100;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/StoreOptionsTests.cs#L279-L288' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting-name-data-length' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom StoreOptions

It's perfectly valid to create your own subclass of `StoreOptions` that configures itself, as shown below.

<!-- snippet: sample_custom-store-options -->
<a id='snippet-sample_custom-store-options'></a>
```cs
public class MyStoreOptions: StoreOptions
{
    public static IDocumentStore ToStore()
    {
        return new DocumentStore(new MyStoreOptions());
    }

    public MyStoreOptions()
    {
        Connection(ConnectionSource.ConnectionString);

        Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });

        Schema.For<User>().Index(x => x.UserName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L210-L228' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-store-options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This strategy might be beneficial if you need to share Marten configuration across different applications
or testing harnesses or custom migration tooling.

## MartenRegistry

While there are some limited abilities to configure storage with attributes, the most complete option right now
is a fluent interface implemented by the `MartenRegistry`. To configure a Marten document store, first write
your own subclass of `MartenRegistry` and place declarations in the constructor function like this example:

<[sample:MyMartenRegistry]>

To apply your new `MartenRegistry`, just include it when you bootstrap the `IDocumentStore` as in this example:

<!-- snippet: sample_using_marten_registry_to_bootstrap_document_store -->
<a id='snippet-sample_using_marten_registry_to_bootstrap_document_store'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("your connection string");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L11-L16' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_marten_registry_to_bootstrap_document_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that you could happily use multiple `MartenRegistry` classes in larger applications if that is advantageous.

If you dislike using infrastructure attributes in your application code, you will probably prefer to use MartenRegistry.

## Custom Indexes

If you intend to write your own indexes against Marten document tables, just ensure that the index names are **not** prefixed with "mt_" so
that Marten will ignore your manual indexes when calculating schema differences.

## Custom Attributes

If there's some kind of customization you'd like to use attributes for that isn't already supported by Marten,
you're still in luck. If you write a subclass of the `MartenAttribute` shown below:

<!-- snippet: sample_MartenAttribute -->
<a id='snippet-sample_martenattribute'></a>
```cs
public abstract class MartenAttribute: Attribute
{
    /// <summary>
    /// Customize Document storage at the document level
    /// </summary>
    /// <param name="mapping"></param>
    public virtual void Modify(DocumentMapping mapping) { }

    /// <summary>
    /// Customize the Document storage for a single member
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="member"></param>
    public virtual void Modify(DocumentMapping mapping, MemberInfo member) { }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/MartenAttribute.cs#L10-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_martenattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And decorate either classes or individual field or properties on a document type, your custom attribute will be
picked up and used by Marten to configure the underlying `DocumentMapping` model for that document type. The
`MartenRegistry` is just a fluent interface over the top of this same `DocumentMapping` model.

As an example, an attribute to add a gin index to the JSONB storage for more efficient adhoc querying of a document
would look like this:

<!-- snippet: sample_GinIndexedAttribute -->
<a id='snippet-sample_ginindexedattribute'></a>
```cs
[AttributeUsage(AttributeTargets.Class)]
public class GinIndexedAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.AddGinIndexToData();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/GinIndexedAttribute.cs#L9-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ginindexedattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Embedding Configuration in Document Types

Lastly, Marten can examine the document types themselves for a `public static ConfigureMarten()` method
and invoke that to let the document type make its own customizations for its storage. Here's an example from
the unit tests:

<!-- snippet: sample_ConfigureMarten-generic -->
<a id='snippet-sample_configuremarten-generic'></a>
```cs
public class ConfiguresItself
{
    public Guid Id;

    public static void ConfigureMarten(DocumentMapping mapping)
    {
        mapping.Alias = "different";
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentMappingTests.cs#L123-L134' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-generic' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `DocumentMapping` type is the core configuration class representing how a document type is persisted or
queried from within a Marten application. All the other configuration options end up writing to a
`DocumentMapping` object.

You can optionally take in the more specific `DocumentMapping<T>` for your document type to get at
some convenience methods for indexing or duplicating fields that depend on .Net Expression's:

<!-- snippet: sample_ConfigureMarten-specifically -->
<a id='snippet-sample_configuremarten-specifically'></a>
```cs
public class ConfiguresItselfSpecifically
{
    public Guid Id;
    public string Name;

    public static void ConfigureMarten(DocumentMapping<ConfiguresItselfSpecifically> mapping)
    {
        mapping.Duplicate(x => x.Name);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentMappingTests.cs#L136-L148' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-specifically' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
