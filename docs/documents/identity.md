# Document Identity

Besides being serializable, Marten's only other requirement for a .Net type to be a document is the existence of an identifier field or property that Marten can use as the primary key for the document type. The `Id` can be either a public field or property, and the name must be either `id` or `Id` or `ID`. As of this time, Marten supports these `Id` types:

1. `String`. It might be valuable to use a [natural key](https://en.wikipedia.org/wiki/Natural_key) as the identifier, especially if it is valuable within the
   [Identity Map](/documents/identity) feature of Marten Db. In this case, the user will
   be responsible for supplying the identifier.
1. `Guid`. If the id is a Guid, Marten will assign a new value for you when you persist the document for the first time if the id is empty.
   _And for the record, it's pronounced "gwid"_.
1. `CombGuid` is a [sequential Guid algorithm](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs). It can improve performance over the default Guid as it reduces fragmentation of the PK index.
1. `Int` or `Long`. As of right now, Marten uses a [HiLo generator](http://stackoverflow.com/questions/282099/whats-the-hi-lo-algorithm) approach to assigning numeric identifiers by document type.
   Marten may support Postgresql sequences or star-based algorithms as later alternatives.
1. When the ID member of a document is not settable or not-public a `NoOpIdGeneration` strategy is used. This ensures that Marten does not set the ID itself, so the ID should be generated manually.
1. A `Custom` ID generator strategy is used to implement the ID generation strategy yourself.

Marten by default uses the identity value set on documents and only assigns one in case it has no value (`Guid.Empty`, `0`, `string.Empty` etc).

::: tip INFO
When using a `Guid`/`CombGuid`, `Int`, or `Long` identifier, Marten will ensure the identity is set immediately after calling `IDocumentSession.Store` on the entity.
:::

You can see some example id usages below:

<!-- snippet: sample_id_samples -->
<a id='snippet-sample_id_samples'></a>
```cs
public class Division
{
    // String property as Id
    public string Id { get; set; }
}

public class Category
{
    // Guid's work, fields too
    public Guid Id;
}

public class Invoice
{
    // int's and long's can be the Id
    // "id" is accepted
    public int id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IdExamples.cs#L5-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_id_samples' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Overriding the Choice of Id Property/Field

If you really want to or you're migrating existing document types from another document database, Marten provides
the `[Identity]` attribute to force Marten to use a property or field as the identifier that doesn't match
the "id" or "Id" or "ID" convention:

<!-- snippet: sample_IdentityAttribute -->
<a id='snippet-sample_identityattribute'></a>
```cs
public class NonStandardDoc
{
    [Identity]
    public string Name;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/using_natural_identity_keys.cs#L72-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_identityattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The identity property or field can also be configured through `StoreOptions` by using the `Schema` to obtain a document mapping:

<!-- snippet: sample_sample-override-id-fluent-interance -->
<a id='snippet-sample_sample-override-id-fluent-interance'></a>
```cs
storeOptions.Schema.For<OverriddenIdDoc>().Identity(x => x.Name);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/using_natural_identity_keys.cs#L56-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-override-id-fluent-interance' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Guid Identifiers

::: tip INFO
As of Marten 1.0, the default Guid mechanism is a sequential or "Comb" Guid. While more expensive to
generate, this makes inserts into the underlying document tables more efficient.
:::

To use _CombGuid_ generation you should enabled it when configuring the document store. This defines that the _CombGuid_ generation strategy will be used for all the documents types.

<!-- snippet: sample_configuring-global-sequentialguid -->
<a id='snippet-sample_configuring-global-sequentialguid'></a>
```cs
options.Policies.ForAllDocuments(m =>
{
    if (m.IdType == typeof(Guid))
    {
        m.IdStrategy = new CombGuidIdGeneration();
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CombGuidIdGenerationTests.cs#L43-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-sequentialguid -->
<a id='snippet-sample_configuring-mapping-specific-sequentialguid'></a>
```cs
options.Schema.For<UserWithGuid>().IdStrategy(new CombGuidIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CombGuidIdGenerationTests.cs#L76-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Sequential Identifiers with Hilo

The _Hilo_ sequence generation can be customized with either global defaults or document type specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

To set different global defaults, use the `StoreOptions.HiloSequenceDefaults` property like this sample:

<!-- snippet: sample_configuring-global-hilo-defaults -->
<a id='snippet-sample_configuring-global-hilo-defaults'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Advanced.HiloSequenceDefaults.MaxLo = 55;
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L63-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It's also possible to use one sequence with multiple document types by specifying the same "sequence name".

<!-- snippet: sample_configuring-global-hilo-defaults-sequencename -->
<a id='snippet-sample_configuring-global-hilo-defaults-sequencename'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Advanced.HiloSequenceDefaults.SequenceName = "Entity";
    _.Connection(ConnectionSource.ConnectionString);

    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L149-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults-sequencename' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To override the Hilo configuration for a specific document type, you can decorate the document type with the `[HiloSequence]` attribute
as in this example:

<!-- snippet: sample_overriding-hilo-with-attribute -->
<a id='snippet-sample_overriding-hilo-with-attribute'></a>
```cs
[HiloSequence(MaxLo = 66, SequenceName = "Entity")]
public class OverriddenHiloDoc
{
    public int Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L187-L193' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use the `MartenRegistry` fluent interface to override the Hilo configuration for a document type as in this example:

<!-- snippet: sample_overriding-hilo-with-marten-registry -->
<a id='snippet-sample_overriding-hilo-with-marten-registry'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Overriding the Hilo settings for the document type "IntDoc"
    _.Schema.For<IntDoc>()
        .HiloSettings(new HiloSettings {MaxLo = 66});

    _.Connection(ConnectionSource.ConnectionString);

    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L79-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-marten-registry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Set the HiLo Identifier Floor

Marten 1.2 adds a convenience method to reset the "floor" of the Hilo sequence for a single document type:

<!-- snippet: sample_ResetHiloSequenceFloor -->
<a id='snippet-sample_resethilosequencefloor'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.DatabaseSchemaName = "sequences";
});

// Resets the minimum Id number for the IntDoc document
// type to 2500
await store.Tenancy.Default.Storage.ResetHiloSequenceFloor<IntDoc>(2500);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L18-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_resethilosequencefloor' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This functionality was added specifically to aid in importing data from an existing data source. Do note that this functionality simply guarantees
that all new id's assigned for the document type will be higher than the new floor. It is perfectly possible and even likely that there will be some
gaps in the id sequence.

## String Identity

If you use a document type with a `string` identity member, you will be responsible for supplying the
identity value to Marten on any object passed to any storage API like `IDocumentSession.Store()`. You can
choose to use the _Identity Key_ option for automatic identity generation as shown in the next section.

## Identity Key

::: warning
The document alias is also used to name the underlying Postgresql table and functions for this document type,
so you will not be able to use any kind of punctuation characters or spaces.
:::

Let's say you have a document type with a `string` for the identity member like this one:

<!-- snippet: sample_DocumentWithStringId -->
<a id='snippet-sample_documentwithstringid'></a>
```cs
public class DocumentWithStringId
{
    public string Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/IdentityKeyGenerationTests.cs#L28-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_documentwithstringid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can use the "identity key" option for identity generation that would create string values of the pattern `[type alias]/[sequence]` where the type alias is typically the document class name in all lower case and the sequence is a *HiLo* sequence number.

You can opt into the *identity key* strategy for identity and even override the document alias name with this syntax:

<!-- snippet: sample_using_IdentityKey -->
<a id='snippet-sample_using_identitykey'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.Schema.For<DocumentWithStringId>()
        .UseIdentityKey()
        .DocumentAlias("doc");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/IdentityKeyGenerationTests.cs#L39-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_identitykey' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Identity Strategies

A custom ID generator strategy should implement [IIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/IIdGeneration.cs).

<!-- snippet: sample_custom-id-generation -->
<a id='snippet-sample_custom-id-generation'></a>
```cs
public class CustomIdGeneration : IIdGeneration
{
    public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

    public bool RequiresSequences { get; } = false;
    public void GenerateCode(GeneratedMethod assign, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        assign.Frames.Code($"_setter({{0}}, \"newId\");", document);
        assign.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L14-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-id-generation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Build()` method should return the actual `IdGenerator<T>` for the document type, where `T` is the type of the Id field.

For more advances examples you can have a look at existing ID generator: [HiloIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/HiloIdGeneration.cs), [CombGuidGenerator](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs) and the [IdentityKeyGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/IdentityKeyGeneration.cs),

To use custom id generation you should enabled it when configuring the document store. This defines that the strategy will be used for all the documents types.

<!-- snippet: sample_configuring-global-custom -->
<a id='snippet-sample_configuring-global-custom'></a>
```cs
options.Policies.ForAllDocuments(m =>
{
    if (m.IdType == typeof(string))
    {
        m.IdStrategy = new CustomIdGeneration();
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L37-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible define a custom id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-custom -->
<a id='snippet-sample_configuring-mapping-specific-custom'></a>
```cs
options.Schema.For<UserWithString>().IdStrategy(new CustomIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L68-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
