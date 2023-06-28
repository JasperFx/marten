# Configuring Document Storage with StoreOptions

The `StoreOptions` object in Marten is the root of all of the configuration for a `DocumentStore` object.
The static builder methods like `DocumentStore.For(configuration)` or `IServiceCollection.AddMarten(configuration)` are just
syntactic sugar around building up a `StoreOptions` object and passing that to the constructor function of a `DocumentStore`:

<!-- snippet: sample_DocumentStore.For -->
<a id='snippet-sample_documentstore.for'></a>
```cs
public static DocumentStore For(Action<StoreOptions> configure)
{
    var options = new StoreOptions();
    configure(options);

    return new DocumentStore(options);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/DocumentStore.cs#L493-L503' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_documentstore.for' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The major parts of `StoreOptions` are shown in the class diagram below:

![StoreOptions](/images/StoreOptions.png)

For some explanation, the major pieces are:

* `EventGraph` -- The configuration for the Event Store functionality is all on the `StoreOptions.Events` property. See the [Event Store documentation](/events/) for more information.
* `DocumentMapping` -- This is the configuration for a specific document type including all indexes and rules for multi-tenancy, deletes, and metadata usage
* `MartenRegistry` -- The `StoreOptions.Schema` property is a `MartenRegistry` that provides a fluent interface to explicitly configure document storage by document type
* `IDocumentPolicy` -- Registered policies on a `StoreOptions` object that apply to all document types. An example would be "all document types are soft deleted."
* `MartenAttribute` -- Document type configuration can also be done with attributes on the actual document types

To be clear, the configuration on a single document type is applied in order by:

1. Calling the static `ConfigureMarten(DocumentMapping)` method on the document type. See the section below on _Embedding Configuration in Document Types_
1. Any policies at the `StoreOptions` level
1. Attributes on the specific document type
1. Explicit configuration through `MartenRegistry`

The order of precedence is in the reverse order, such that explicit configuration takes precedence over policies or attributes.

::: tip
While it is possible to mix and match configuration styles, the Marten team recommends being consistent in your approach to prevent
confusion later.
:::

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L197-L215' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-store-options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This strategy might be beneficial if you need to share Marten configuration across different applications
or testing harnesses or custom migration tooling.

## Explicit Document Configuration with MartenRegistry

While there are some limited abilities to configure storage with attributes, the most complete option right now
is a fluent interface implemented by the `MartenRegistry` that is exposed from the `StoreOptions.Schema` property, or you can choose
to compose your document type configuration in additional `MartenRegistry` objects.

To use your own subclass of `MartenRegistry` and place declarations in the constructor function like this example:

<!-- snippet: sample_OrganizationRegistry -->
<a id='snippet-sample_organizationregistry'></a>
```cs
public class OrganizationRegistry: MartenRegistry
{
    public OrganizationRegistry()
    {
        For<Organization>().Duplicate(x => x.OtherName);
        For<User>().Duplicate(x => x.UserName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L138-L149' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_organizationregistry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To apply your new `MartenRegistry`, just include it when you bootstrap the `IDocumentStore` as in this example:

<!-- snippet: sample_including_a_custom_MartenRegistry -->
<a id='snippet-sample_including_a_custom_martenregistry'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Schema.For<Organization>().Duplicate(x => x.Name);
    opts.Schema.Include<OrganizationRegistry>();
    opts.Connection(ConnectionSource.ConnectionString);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L171-L180' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_including_a_custom_martenregistry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that you could happily use multiple `MartenRegistry` classes in larger applications if that is advantageous.

If you dislike using infrastructure attributes in your application code, you will probably prefer to use MartenRegistry.

Lastly, note that you can use `StoreOptions.Schema` property for all configuration like this:

<!-- snippet: sample_using_storeoptions_schema -->
<a id='snippet-sample_using_storeoptions_schema'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.Schema.For<Organization>()
        .Duplicate(x => x.OtherName);

    opts.Schema
        .For<User>().Duplicate(x => x.UserName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L153-L165' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_storeoptions_schema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Attributes

If there's some kind of customization you'd like to use attributes for that isn't already supported by Marten,
you're still in luck. If you write a subclass of the `MartenAttribute` shown below:

<!-- snippet: sample_MartenAttribute -->
<a id='snippet-sample_martenattribute'></a>
```cs
public abstract class MartenAttribute: Attribute
{
    /// <summary>
    ///     Customize Document storage at the document level
    /// </summary>
    /// <param name="mapping"></param>
    public virtual void Modify(DocumentMapping mapping) { }

    /// <summary>
    ///     Customize the Document storage for a single member
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="member"></param>
    public virtual void Modify(DocumentMapping mapping, MemberInfo member) { }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/MartenAttribute.cs#L12-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_martenattribute' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/GinIndexedAttribute.cs#L10-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ginindexedattribute' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L126-L138' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-generic' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L140-L153' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-specifically' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Document Policies

Document Policies enable convention-based customizations to be applied across the Document Store. While Marten has some existing policies that can be enabled, any custom policy can be introduced  through implementing the `IDocumentPolicy` interface and applying it on `StoreOptions.Policies` or through using the `Policies.ForAllDocuments(Action<DocumentMapping> configure)` shorthand.

The following sample demonstrates a policy that sets types implementing `IRequireMultiTenancy` marker-interface to be multi-tenanted (see [tenancy](/documents/multi-tenancy)).

<!-- snippet: sample_sample-policy-configure -->
<a id='snippet-sample_sample-policy-configure'></a>
```cs
var store = DocumentStore.For(storeOptions =>
{
    // Apply custom policy
    storeOptions.Policies.OnDocuments<TenancyPolicy>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L19-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-configure' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The actual policy is shown below:

<!-- snippet: sample_sample-policy-implementation -->
<a id='snippet-sample_sample-policy-implementation'></a>
```cs
public interface IRequireMultiTenancy
{
}

public class TenancyPolicy: IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.DocumentType.GetInterfaces().Any(x => x == typeof(IRequireMultiTenancy)))
        {
            mapping.TenancyStyle = TenancyStyle.Conjoined;
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L31-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-implementation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To set all types to be multi-tenanted, the pre-baked `Policies.AllDocumentsAreMultiTenanted` could also have been used.

Remarks: Given the sample, you might not want to let tenancy concerns propagate to your types in a real data model.

## Configuring the Database Schema

By default, Marten will put all database schema objects into the main _public_ schema. If you want to override this behavior,
use the `StoreOptions.DocumentSchemaName` property when configuring your `IDocumentStore`:

<!-- snippet: sample_setting_database_schema_name -->
<a id='snippet-sample_setting_database_schema_name'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.DatabaseSchemaName = "other";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDatabaseSchemaName.cs#L9-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_database_schema_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you have some reason to place different document types into separate schemas, that is also supported and the document type specific configuration will override the `StoreOptions.DatabaseSchemaName` value as shown below:

<!-- snippet: sample_configure_schema_by_document_type -->
<a id='snippet-sample_configure_schema_by_document_type'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.DatabaseSchemaName = "other";

    // This would take precedence for the
    // User document type storage
    opts.Schema.For<User>()
        .DatabaseSchemaName("users");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDatabaseSchemaName.cs#L22-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_schema_by_document_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Postgres Limits on Naming

Postgresql has a default limitation on the length of database object names (64). This can be overridden in a
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/StoreOptionsTests.cs#L284-L293' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting-name-data-length' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
