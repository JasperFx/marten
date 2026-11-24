# Configuring Document Storage with StoreOptions

The `StoreOptions` object in Marten is the root of all of the configuration for a `DocumentStore` object.
The static builder methods like `DocumentStore.For(configuration)` or `IServiceCollection.AddMarten(configuration)` are just
syntactic sugar around building up a `StoreOptions` object and passing that to the constructor function of a `DocumentStore`:

<!-- snippet: sample_documentstore.For -->
<a id='snippet-sample_documentstore.For'></a>
```cs
public static DocumentStore For(Action<StoreOptions> configure)
{
    var options = new StoreOptions();
    configure(options);

    return new DocumentStore(options);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/DocumentStore.cs#L806-L816' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_documentstore.For' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L213-L231' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-store-options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This strategy might be beneficial if you need to share Marten configuration across different applications
or testing harnesses or custom migration tooling.

## Restoring Earlier Defaults

Each major version of Marten has changed some `StoreOptions` defaults. If you are upgrading and want the
previous behavior back while you work through the changes, there is one call per major version:

<!-- snippet: sample_restoring_earlier_defaults -->
<a id='snippet-sample_restoring_earlier_defaults'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Put back every default that Marten 10 changed...
    opts.RestoreV9Defaults();

    // ...and, to go back two majors, the ones Marten 9 changed
    opts.RestoreV8Defaults();

    // Your own overrides go after the restore calls
    opts.Events.EnableExtendedProgressionTracking = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L72-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_restoring_earlier_defaults' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* `RestoreV9Defaults()` puts back every `StoreOptions` default that Marten 10 changed. See
  [Restoring V9 defaults](/migration-guide#restoring-v9-defaults) for the list.
* `RestoreV8Defaults()` puts back the `StoreOptions` defaults that Marten 9 changed. See
  [Restoring V8 defaults](/migration-guide#restoring-v8-defaults) for the list, and for the two Marten 9 changes it cannot reach.

`RestoreV9Defaults()` does not call `RestoreV8Defaults()`, so call both to go back two major versions. Both assign
the settings they cover, so call them first and put your own settings after them.

## Explicit Document Configuration with MartenRegistry

While there are some limited abilities to configure storage with attributes, the most complete option right now
is a fluent interface implemented by the `MartenRegistry` that is exposed from the `StoreOptions.Schema` property, or you can choose
to compose your document type configuration in additional `MartenRegistry` objects.

To use your own subclass of `MartenRegistry` and place declarations in the constructor function like this example:

<!-- snippet: sample_organizationregistry -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L173-L184' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_organizationregistry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To apply your new `MartenRegistry`, just include it when you bootstrap the `IDocumentStore` as in this example:

<!-- snippet: sample_including_a_custom_martenregistry -->
<a id='snippet-sample_including_a_custom_martenregistry'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Schema.For<Organization>().Duplicate(x => x.Name);
    opts.Schema.Include<OrganizationRegistry>();
    opts.Connection(ConnectionSource.ConnectionString);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L206-L215' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_including_a_custom_martenregistry' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L188-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_storeoptions_schema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Attributes

If there's some kind of customization you'd like to use attributes for that isn't already supported by Marten,
you're still in luck. If you write a subclass of the `MartenAttribute` shown below:

<!-- snippet: sample_martenattribute -->
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

    /// <summary>
    /// When used with the automatic type discovery (assembly scanning), this will be called
    /// to make registrations to the Marten configuration with the type that this attribute
    /// decorates
    /// </summary>
    /// <param name="discoveredType"></param>
    /// <param name="options"></param>
    public virtual void Register(Type discoveredType, StoreOptions options){}
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/MartenAttribute.cs#L12-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_martenattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And decorate either classes or individual field or properties on a document type, your custom attribute will be
picked up and used by Marten to configure the underlying `DocumentMapping` model for that document type. The
`MartenRegistry` is just a fluent interface over the top of this same `DocumentMapping` model.

As an example, an attribute to add a gin index to the JSONB storage for more efficient adhoc querying of a document
would look like this:

<!-- snippet: sample_ginindexedattribute -->
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

A custom attribute can also override `Register()`. That method is only called by the automatic type discovery
described in the next section, which hands it each type it finds carrying the attribute.

## Automatic Type Discovery with AutoRegister()

Rather than listing every document and event type in your configuration, you can mark the types themselves
and have Marten find them. `StoreOptions.AutoRegister()` scans the application assembly for public, concrete types
decorated with a `MartenAttribute`, and calls that attribute's `Register()` method for each one:

<!-- snippet: sample_auto_register_with_defaults -->
<a id='snippet-sample_auto_register_with_defaults'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Scans the application assembly for types decorated with
    // [MartenDocument], [MartenEvent] or any other MartenAttribute
    // that overrides Register()
    opts.AutoRegister();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L26-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_auto_register_with_defaults' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten ships two attributes made for this. `[MartenDocument]` registers a document type, and `[MartenEvent]`
registers an event type, optionally under an alias of your choosing:

<!-- snippet: sample_marten_document_and_event_attributes -->
<a id='snippet-sample_marten_document_and_event_attributes'></a>
```cs
// Registered as a document type by AutoRegister()
[MartenDocument]
public class ConfigDocsCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// Registered as an event type by AutoRegister()
[MartenEvent]
public record ConfigDocsCustomerRenamed(Guid CustomerId, string Name);

// Registered as an event type, stored under the alias "customer_closed"
[MartenEvent(Alias = "customer_closed")]
public record ConfigDocsCustomerClosed(Guid CustomerId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L332-L350' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_document_and_event_attributes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`[DatabaseSchemaName]`, `[DocumentAlias]`, `[SoftDeleted]` and `[UseOptimisticConcurrency]` derive from
`[MartenDocument]`, so a document type carrying any of them is registered by the scan as well.

The overload taking a `MartenAssemblyScanner` adds more assemblies to the scan, and registers event types by a
rule instead of one attribute at a time:

<!-- snippet: sample_auto_register_with_scanner -->
<a id='snippet-sample_auto_register_with_scanner'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Only needed when Assembly.GetEntryAssembly() is not your
    // application, a test runner for example
    opts.ApplicationAssembly = typeof(ConfigDocsOrderPlaced).Assembly;

    opts.AutoRegister(x =>
    {
        // Scan this assembly too, in addition to the application assembly
        x.Assembly(typeof(ConfigDocsCustomer).Assembly);

        // Every public, concrete type that implements or inherits
        // this type is registered as an event type
        x.EventsImplement<IConfigDocsDomainEvent>();

        // Or decide for yourself
        x.EventsMatch(type => type.Name.EndsWith("Happened"));
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L43-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_auto_register_with_scanner' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

| Scanner method | What it does |
| --- | --- |
| `Assembly(assembly)` | Scans this assembly as well as the application assembly |
| `EventsImplement<T>()` / `EventsImplement(type)` | Registers every scanned type that can be cast to `T` as an event type |
| `EventsMatch(filter)` | Registers every scanned type that your filter accepts as an event type |

Some things to know about the scan:

* It runs when you call `AutoRegister()`, not later when the store is built.
* The application assembly is `StoreOptions.ApplicationAssembly`, or `Assembly.GetEntryAssembly()` when that is not set. Under
  `AddMarten()`, the application assembly from the JasperFx options is only read after your configuration has run,
  so set `ApplicationAssembly` yourself before calling `AutoRegister()` when the entry assembly is not your
  application, as it is not under a test runner.
* Only public types are considered, including public nested types. Abstract types, interfaces and open generic types are skipped.
* The scan also visits every public compiled query type, and throws an `ArgumentOutOfRangeException` for one
  that has no parameterless constructor.

## Embedding Configuration in Document Types

Lastly, Marten can examine the document types themselves for a `public static ConfigureMarten()` method
and invoke that to let the document type make its own customizations for its storage. Here's an example from
the unit tests:

<!-- snippet: sample_configuremarten-generic -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L799-L811' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-generic' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `DocumentMapping` type is the core configuration class representing how a document type is persisted or
queried from within a Marten application. All the other configuration options end up writing to a
`DocumentMapping` object.

You can optionally take in the more specific `DocumentMapping<T>` for your document type to get at
some convenience methods for indexing or duplicating fields that depend on .Net Expression's:

<!-- snippet: sample_configuremarten-specifically -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/DocumentMappingTests.cs#L813-L826' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuremarten-specifically' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L20-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-configure' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L32-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-implementation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To set all types to be multi-tenanted, the pre-baked `Policies.AllDocumentsAreMultiTenanted` could also have been used.

Remarks: Given the sample, you might not want to let tenancy concerns propagate to your types in a real data model.

## Configuring the Database Schema

By default, Marten will put all database schema objects into the main _public_ schema. If you want to override this behavior,
use the `StoreOptions.DatabaseSchemaName` property when configuring your `IDocumentStore`:

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

Marten lower-cases the name you give `DatabaseSchemaName`, and cuts it to PostgreSQL's 63 character identifier
limit. PostgreSQL would truncate a longer name anyway, so this only makes Marten look for its objects under the
name the server actually stored. Two long names that share their first 63 characters still end up in the same schema.

## PostgreSQL Limits on Naming

PostgreSQL limits the names of database objects to 63 bytes, one less than its `NAMEDATALEN` setting, which is 64
unless your PostgreSQL server was compiled with a different value. See
[the PostgreSQL documentation on identifiers](https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS).

PostgreSQL quietly truncates a longer name, which would leave Marten unable to match the objects it finds in the
database against its configuration. To guard against this, Marten checks the names of the objects it is about to
create and throws a `PostgresqlIdentifierTooLongException` for any name of 64 characters or more. That limit comes
from `NameDataLength`, which defaults to 64, so if your server was built with a different `NAMEDATALEN`, tell Marten:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/StoreOptionsTests.cs#L323-L332' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting-name-data-length' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Other StoreOptions Settings

These are less commonly needed settings directly on `StoreOptions`, or on `StoreOptions.Advanced` where noted:

<!-- snippet: sample_less_common_store_options -->
<a id='snippet-sample_less_common_store_options'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Lower-cased, and cut to PostgreSQL's 63 character identifier limit
    opts.DatabaseSchemaName = "Billing";

    // Names the store in monitoring and diagnostics. Defaults to "Main"
    opts.StoreName = "Billing";

    // The Event Model this store's projections contribute their slices to
    opts.EventModelName = "Billing";

    // How many operations SaveChangesAsync() sends to PostgreSQL in one
    // batched command. Defaults to 500
    opts.UpdateBatchSize = 250;

    // How long ApplyAllDatabaseChangesOnStartup() waits for the migration
    // advisory lock. Defaults to 10 seconds
    opts.ApplyChangesLockTimeout = 30.Seconds();

    // Applied to the serializer now, and again to any serializer that
    // replaces it later in the configuration
    opts.Advanced.ModifySerializer(serializer =>
    {
        if (serializer is SystemTextJsonSerializer stj)
        {
            stj.Configure(o => o.Converters.Add(new JsonStringEnumConverter()));
        }
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L113-L147' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_less_common_store_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

| Member | Default | What it does |
| --- | --- | --- |
| `DatabaseSchemaName` | `public` | The schema for Marten's objects, unless a document type names its own. Lower-cased, and cut to 63 characters ([more above](#configuring-the-database-schema)) |
| `StoreName` | `Main` | Names the store. It becomes the store's identity and its `marten://` subject URI, which monitoring tools such as CritterWatch use to tell stores apart, and the identifier of a single-database store's database. A store added with `AddMartenStore<T>()` is always named after `T`, whatever you set here |
| `EventModelName` | `null` | Which Event Model this store's projections contribute their slices to. See [Contributing to the Event Model](/configuration/hostbuilder#contributing-to-the-event-model) |
| `UpdateBatchSize` | `500` | The most operations Marten sends to PostgreSQL in one batched command. A `SaveChangesAsync()` with more is sent as several commands, and the async daemon pages its projection updates by the same number |
| `ApplyChangesLockTimeout` | 10 seconds | How long `ApplyAllDatabaseChangesOnStartup()` keeps trying for the advisory lock that stops two nodes migrating at once. See [Schema Migrations](/schema/migrations#applying-changes-at-startup) |
| `ApplyChangesLockId` | `4004` | The id of that advisory lock |
| `ResourceMigrationFailureMode` | the JasperFx profile's value, `FailFast` | Whether a failed migration at startup stops the host. See [Schema Migrations](/schema/migrations#applying-changes-at-startup) |
| `DisableNpgsqlLogging` | `true` | Keeps Npgsql's own logging out of your logs. Set it to `false` to forward Npgsql's logging to the application's `ILoggerFactory` under `AddMarten()` |
| `ConfigureNpgsqlDataSourceBuilder(configure)` | | Applies `configure` to every `NpgsqlDataSourceBuilder` Marten creates, including the ones for tenant databases. This is where Npgsql plugins such as `UseVector()` or `UseNetTopologySuite()` are registered, and it is what Marten's own pgvector and PostGIS packages call. A data source you passed to `Connection(NpgsqlDataSource)` is used as it is |
| `DataSourceFactory(factory, connectionString)` | Marten's own | Replaces the Weasel `INpgsqlDataSourceFactory` that builds an `NpgsqlDataSource` for each connection string Marten uses. The optional connection string is passed to `Connection()` if no connection or tenancy has been configured yet. `ConfigureNpgsqlDataSourceBuilder()` replaces the factory, so set a custom one after it |
| `Advanced.ModifySerializer(configure)` | | Applies `configure` to the current serializer straight away, and again to any serializer that replaces it later. Meant for extensions that need to adjust the serializer whichever one the application chooses |

## Prepared Statements and `Max Auto Prepare`

Marten does not use PostgreSQL prepared statements, and there is no Marten API to turn them on. That
is a deliberate choice rather than a gap, and this section exists to record both the option you do
have and why it is not the default.

Npgsql can prepare statements automatically, and it is enabled purely through the connection string,
so it needs nothing from Marten:

```text
Host=localhost;Database=marten;Username=postgres;Password=...;Max Auto Prepare=50;Auto Prepare Min Usages=2
```

Nothing in Marten blocks the mechanism. Marten binds values as parameters throughout — including
`LIMIT` and `OFFSET` — so the generated SQL text is stable across calls and pages, and auto-prepare
will genuinely reuse plans rather than thrash.

### Set your expectations low

We measured it. On a warm connection, past the minimum-usage threshold:

| query | default | `Max Auto Prepare=50` |
| --- | --- | --- |
| unindexed JSONB scan returning 10 rows | 40,102 μs | 39,840 μs |
| indexed duplicated column returning 1 row | 430 μs | 365 μs |
| the same single row via a compiled query | 364 μs | 391 μs |

Those land on both sides of zero with overlapping error bars, which is to say there was no
measurable effect on read latency. That fits the mechanism: `PREPARE` saves the server's parse and
plan time for a statement, which for a simple single-table query is tens of microseconds, against a
cost dominated by JSONB extraction, sorting, moving documents over the wire, and deserializing them
on the client.

### The async daemon's event page

The async daemon's hottest statement is the one that loads each page of events: the same SQL text on
every page, with only the sequence floor and ceiling bound as parameters, so `Max Auto Prepare` does
pick it up. We measured it against an explicit `Prepare()` of the same statement, on a warm pool,
loading events from a 20,000 event store:

| events in the page | raw statement | `Prepare()` | `Max Auto Prepare=20` | daemon page load, default | daemon page load, `Max Auto Prepare=20` |
| --- | --- | --- | --- | --- | --- |
| 1 | 201 μs | 200 μs | 202 μs | 199 μs | 186 μs |
| 100 | 452 μs | 404 μs | 408 μs | 553 μs | 439 μs |
| 500 | 786 μs | 803 μs | 817 μs | 1,054 μs | 1,081 μs |

Preparing saves the server's parse and plan of the statement, a few tens of microseconds, and about
1 KB of allocation per page. That shows up only where the round trip is a large share of the page:
a mid-sized page here, never a single event and never a full 500 event page, where moving and
deserializing the events dominates. An explicit `Prepare()` bought nothing that `Max Auto Prepare`
did not, which is why Marten adds no daemon-specific switch for it. The cautions below apply to the
daemon with one addition: a projection that filters on event types binds the type names as a
parameter, which is exactly where a generic plan can go wrong on a store whose event types are
heavily skewed.

### Why it is not on by default

The reasons to be careful are more specific than the reasons to be hopeful:

* **Generic plans.** After a handful of executions PostgreSQL may switch a prepared statement to a
  generic plan that ignores the actual parameter values. If you use conjoined multi-tenancy, a
  heavily skewed `tenant_id` appears in nearly every predicate — exactly the data shape where a
  generic plan can be dramatically worse than a fresh one.
* **Memory is per connection.** Prepared statements are cached on each physical connection, so the
  server-side cost scales with pool size multiplied by `Max Auto Prepare`.
* **Runtime schema changes.** Marten applies schema changes at runtime under its automatic schema
  creation modes, and DDL can invalidate plans cached on connections already in the pool.
* **PgBouncer.** Prepared statements are bound to a server connection. In transaction pooling mode
  PgBouncer hands that connection to another client between transactions, so only turn this on
  behind PgBouncer 1.21 or later with `max_prepared_statements` set, or with session pooling.

If you do turn it on, treat it as a per-deployment tuning decision, measure it against your own
workload, and watch for plan regressions on your most skewed columns rather than assuming a win.

## Translating Database Errors into Your Own Exceptions

Marten translates the errors PostgreSQL reports before you see them. A serialization failure (`40001`) becomes a
`ConcurrentUpdateException`, and anything else from Npgsql becomes a `MartenCommandException` carrying the failing SQL.
If your own SQL raises errors of its own, such as a guard function queued with `QueueSqlCommand` that refuses a write
with a custom SQLSTATE, register a transform so your callers receive a typed exception instead of digging through
`InnerException`:

```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    opts.Advanced.ExceptionTransforms.IfExceptionIs<PostgresException>()
        .If(e => e.SqlState == "CT001")
        .ThenTransformTo(e => new GuardRefusedException(e));
});
```

Your transforms are consulted before Marten's built-in ones, on writes, raw SQL and LINQ queries alike. An error none of
them claims still gets Marten's usual translation. A transform that matches an error Marten also translates, such as
`40001`, replaces Marten's translation for that store. Transforms belong to the store they are registered on, never to
the whole process.
