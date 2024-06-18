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
1. _Strong Typed Identifiers_ where a type like the C# `public record struct NewGuidId(Guid Value);` wraps an inner `int`, `long`, `Guid`, or `string` value
1. When the ID member of a document is not settable or not public a `NoOpIdGeneration` strategy is used. This ensures that Marten does not set the ID itself, so the ID should be generated manually.
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

If you really want to, or you're migrating existing document types from another document database, Marten provides
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/using_natural_identity_keys.cs#L73-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_identityattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The identity property or field can also be configured through `StoreOptions` by using the `Schema` to obtain a document mapping:

<!-- snippet: sample_sample-override-id-fluent-interance -->
<a id='snippet-sample_sample-override-id-fluent-interance'></a>
```cs
storeOptions.Schema.For<OverriddenIdDoc>().Identity(x => x.Name);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/using_natural_identity_keys.cs#L57-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-override-id-fluent-interance' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Guid Identifiers

::: tip INFO
As of Marten 1.0, the default Guid mechanism is a sequential or "Comb" Guid. While more expensive to
generate, this makes inserts into the underlying document tables more efficient.
:::

To use _CombGuid_ generation you should enable it when configuring the document store. This defines that the _CombGuid_ generation strategy will be used for all the documents types.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CombGuidIdGenerationTests.cs#L47-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-sequentialguid -->
<a id='snippet-sample_configuring-mapping-specific-sequentialguid'></a>
```cs
options.Schema.For<UserWithGuid>().IdStrategy(new CombGuidIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CombGuidIdGenerationTests.cs#L82-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Sequential Identifiers with Hilo

The _Hilo_ sequence generation can be customized with either global defaults or document type-specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/hilo_configuration_overrides.cs#L63-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/hilo_configuration_overrides.cs#L149-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults-sequencename' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/hilo_configuration_overrides.cs#L186-L192' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-attribute' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/hilo_configuration_overrides.cs#L79-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-marten-registry' title='Start of snippet'>anchor</a></sup>
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
await store.Tenancy.Default.Database.ResetHiloSequenceFloor<IntDoc>(2500);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/hilo_configuration_overrides.cs#L20-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_resethilosequencefloor' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This functionality was added specifically to aid in importing data from an existing data source. Do note that this functionality simply guarantees
that all new IDs assigned for the document type will be higher than the new floor. It is perfectly possible, and even likely, that there will be some
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/IdentityKeyGenerationTests.cs#L30-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_documentwithstringid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can use the "identity key" option for identity generation that would create string values of the pattern `[type alias]/[sequence]` where the type alias is typically the document class name in all lower case and the sequence is a _HiLo_ sequence number.

You can opt into the _identity key_ strategy for identity and even override the document alias name with this syntax:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/IdentityKeyGenerationTests.cs#L41-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_identitykey' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Identity Strategies

A custom ID generator strategy should implement [IIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/IIdGeneration.cs).

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
        assign.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CustomKeyGenerationTests.cs#L16-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-id-generation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Build()` method should return the actual `IdGenerator<T>` for the document type, where `T` is the type of the Id field.

For more advances examples you can have a look at existing ID generator: [HiloIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/HiloIdGeneration.cs), [CombGuidGenerator](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs) and the [IdentityKeyGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/IdentityKeyGeneration.cs),

To use custom id generation you should enable it when configuring the document store. This defines that the strategy will be used for all the documents types.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CustomKeyGenerationTests.cs#L39-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible define a custom id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-custom -->
<a id='snippet-sample_configuring-mapping-specific-custom'></a>
```cs
options.Schema.For<UserWithString>().IdStrategy(new CustomIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CustomKeyGenerationTests.cs#L70-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Strong Typed Identifiers <Badge type="tip" text="7.20" />

::: warning
There are lots of rules in Marten about what can and what can't be used as a strong typed identifier, and this
documentation is trying hard to explain them, but you're best off copying the examples and using something like either
Vogen or StronglyTypedID for now. 
:::

::: info
There is not yet any direct support for strong typed identifiers for the event store
:::

Marten can now support [strong typed identifiers](https://en.wikipedia.org/wiki/Strongly_typed_identifie) using a couple different strategies.
As of this moment, Marten can automatically use types that conform to one of two patterns:

<!-- snippet: sample_valid_strong_typed_identifiers -->
<a id='snippet-sample_valid_strong_typed_identifiers'></a>
```cs
// Use a constructor for the inner value,
// and expose the inner value in a *public*
// property getter
public record struct TaskId(Guid Value);

/// <summary>
/// Pair a public property getter for the inner value
/// with a public static method that takes in the
/// inner value
/// </summary>
public struct Task2Id
{
    private Task2Id(Guid value) => Value = value;

    public Guid Value { get; }

    public static Task2Id From(Guid value) => new Task2Id(value);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/TestingTypes.cs#L29-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_valid_strong_typed_identifiers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In _all_ cases, the type name will have to be suffixed with "Id" (and it's case sensitive) to be considered by Marten to be
a strong typed identity type. The identity types will also need to be immutable `struct` types

The property names or static "builder" methods do not have any requirements for the names, but `Value` and `From` are common.
So far, Marten's strong typed identifier support has been tested with:

1. Hand rolled types, but there's some advantage to using the next two options for JSON serialization, comparisons, and plenty of other goodness
2. [Vogen](https://github.com/SteveDunn/Vogen/tree/main)
3. [StronglyTypedId](https://github.com/andrewlock/StronglyTypedId)

Jumping right into an example, let's say that we want to use this identifier with Vogen for a `Guid`-wrapped
identifier:

<!-- snippet: sample_invoice_with_vogen_id -->
<a id='snippet-sample_invoice_with_vogen_id'></a>
```cs
[ValueObject<Guid>]
public partial struct InvoiceId;

public class Invoice
{
    // Marten will use this for the identifier
    // of the Invoice document
    public InvoiceId? Id { get; set; }
    public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/Vogen/guid_based_document_operations.cs#L287-L300' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_invoice_with_vogen_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The usage of our `Invoice` document is essentially the same as a document type with the primitive identifier types:

<!-- snippet: sample_insert_the_load_by_strong_typed_identifier -->
<a id='snippet-sample_insert_the_load_by_strong_typed_identifier'></a>
```cs
[Fact]
public async Task update_a_document_smoke_test()
{
    var invoice = new Invoice();

    // Just like you're used to with other identity
    // strategies, Marten is able to assign an identity
    // if none is provided
    theSession.Insert(invoice);
    await theSession.SaveChangesAsync();

    invoice.Name = "updated";
    await theSession.SaveChangesAsync();

    // This is a new overload
    var loaded = await theSession.LoadAsync<Invoice>(invoice.Id);
    loaded.Name.ShouldBeNull("updated");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/Vogen/guid_based_document_operations.cs#L84-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_insert_the_load_by_strong_typed_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As you might infer -- or not -- there's a couple rules and internal behavior:

* The identity selection is done just the same as the primitive types, Marten is either looking for an `id`/`Id` member, or a member decorated with
  `[Identity]`
* If Marten is going to assign the identity, you will need to use `Nullable<T>` for the identity member of the document
* There is a new `IQuerySession.LoadAsync<T>(object id)` overload that was specifically built for strong typed identifiers
* For `Guid`-wrapped values, Marten is assigning missing identity values based on its sequential `Guid` support
* For `int` or `long`-wrapped values, Marten is using its HiLo support to define the wrapped values
* For `string`-wrapped values, Marten is going to require you to assign the identity to documents yourself

For another example, here's a usage of an `int` wrapped identifier:

<!-- snippet: sample_order2_with_STRONG_TYPED_identifier -->
<a id='snippet-sample_order2_with_strong_typed_identifier'></a>
```cs
[StronglyTypedId(Template.Int)]
public partial struct Order2Id;

public class Order2
{
    public Order2Id? Id { get; set; }
    public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/StrongTypedId/int_based_document_operations.cs#L262-L273' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order2_with_strong_typed_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Sorry folks, only the asynchronous APIs for loading documents are supported for strong typed identifiers
:::

As of now, Marten supports:

* Loading a single document by its identifier
* Loading multiple documents using `IsOneOf()` as shown below:

<!-- snippet: sample_strong_typed_identifier_and_is_one_of -->
<a id='snippet-sample_strong_typed_identifier_and_is_one_of'></a>
```cs
[Fact]
public async Task load_many()
{
    var issue1 = new Issue2{Name = Guid.NewGuid().ToString()};
    var issue2 = new Issue2{Name = Guid.NewGuid().ToString()};
    var issue3 = new Issue2{Name = Guid.NewGuid().ToString()};
    theSession.Store(issue1, issue2, issue3);

    await theSession.SaveChangesAsync();

    var results = await theSession.Query<Issue2>()
        .Where(x => x.Id.IsOneOf(issue1.Id, issue2.Id, issue3.Id))
        .ToListAsync();

    results.Count.ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/StrongTypedId/long_based_document_operations.cs#L130-L149' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_strong_typed_identifier_and_is_one_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
LoadManyAsync()_ is not supported for strong typed identifiers
:::

* Deleting a document by identity
* Deleting a document by the document itself
* Within `Include()` queries:

<!-- snippet: sample_include_a_single_reference_with_strong_identifier -->
<a id='snippet-sample_include_a_single_reference_with_strong_identifier'></a>
```cs
[Fact]
public async Task include_a_single_reference()
{
    var teacher = new Teacher();
    var c = new Class();

    theSession.Store(teacher);

    c.TeacherId = teacher.Id;
    theSession.Store(c);

    await theSession.SaveChangesAsync();

    theSession.Logger = new TestOutputMartenLogger(_output);

    var list = new List<Teacher>();

    var loaded = await theSession
        .Query<Class>()
        .Include<Teacher>(c => c.TeacherId, list)
        .Where(x => x.Id == c.Id)
        .FirstOrDefaultAsync();

    loaded.Id.ShouldBe(c.Id);
    list.Single().Id.ShouldBe(teacher.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/include_usage.cs#L47-L76' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_include_a_single_reference_with_strong_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* Within LINQ `Where()` clauses
* Within LINQ `Select()` clauses
* Within LINQ `OrderBy()` clauses
* Identity map resolution
* Automatic dirty checks

### LINQ Support

There's a possible timing issue with the strong typed identifiers. Every time that Marten evaluates the identity strategy
for a document that uses a strong typed identifier, Marten "remembers" that that type is a custom value type and will always
treat any usage of that value type as being the actual wrapped value when constructing any SQL. You _might_ need to 
help out Marten a little bit by telling Marten ahead of time about value types before it tries to evaluate any LINQ 
expressions that use members that are value types like so:

<!-- snippet: sample_limited_doc -->
<a id='snippet-sample_limited_doc'></a>
```cs
[ValueObject<int>]
public partial struct UpperLimit;

[ValueObject<int>]
public partial struct LowerLimit;

public class LimitedDoc
{
    public Guid Id { get; set; }
    public UpperLimit Upper { get; set; }
    public LowerLimit Lower { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L73-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_limited_doc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the `UpperLimit` and `LowerLimit` value types can be registered with Marten like so:

<!-- snippet: sample_registering_value_types -->
<a id='snippet-sample_registering_value_types'></a>
```cs
// opts is a StoreOptions just like you'd have in
// AddMarten() calls
opts.RegisterValueType(typeof(UpperLimit));
opts.RegisterValueType(typeof(LowerLimit));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L16-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_value_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And that will enable you to seamlessly use the value types in LINQ expressions like so:

<!-- snippet: sample_using_value_type_in_linq -->
<a id='snippet-sample_using_value_type_in_linq'></a>
```cs
[Fact]
public async Task store_several_and_order_by()
{
    var doc1 = new LimitedDoc { Lower = LowerLimit.From(1), Upper = UpperLimit.From(20) };
    var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25) };
    var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15) };
    var doc4 = new LimitedDoc { Lower = LowerLimit.From(3), Upper = UpperLimit.From(10) };

    theSession.Store(doc1, doc2, doc3, doc4);
    await theSession.SaveChangesAsync();

    var ordered = await theSession
        .Query<LimitedDoc>()
        .OrderBy(x => x.Lower)
        .Select(x => x.Id)
        .ToListAsync();

    ordered.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc3.Id, doc2.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L27-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_value_type_in_linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
