# Document Identity

Besides being serializable, Marten's only other requirement for a .Net type to be a document is the existence of an identifier field or property that Marten can use as the primary key for the document type. The `Id` can be either a public field or property, and the name must be either `id` or `Id` or `ID`. As of this time, Marten supports these `Id` types:

1. `String`. It might be valuable to use a [natural key](https://en.wikipedia.org/wiki/Natural_key) as the identifier, especially if it is valuable within the
   [Identity Map](/documents/sessions#identity-map-mechanics) feature of Marten Db. In this case, the user will
   be responsible for supplying the identifier.
1. `Guid`. If the id is a Guid, Marten will assign a new value for you when you persist the document for the first time if the id is empty.
1. `Int` or `Long`. As of right now, Marten uses a [HiLo generator](http://stackoverflow.com/questions/282099/whats-the-hi-lo-algorithm) approach to assigning numeric identifiers by document type.
   Marten may support Postgresql sequences or star-based algorithms as later alternatives.
1. _Strong Typed Identifiers_ where a type like the C# `public record struct NewGuidId(Guid Value);` wraps an inner `int`, `long`, `Guid`, or `string` value
1. A single-case F# discriminated union can be used as the identifier. In this case, only `Guid` and `string` can be used as inner types, and you must set the ID yourself before saving the document (the ID cannot be automatically set by Marten since F# records are immutable and do not have setters - this would require deep copying arbitrary document types dynamically which is a relatively complex feature not supported at this time)
1. When the ID member of a document is not settable or not public a `NoOpIdGeneration` strategy is used. This ensures that Marten does not set the ID itself, so the ID should be generated manually.

Marten by default uses the identity value set on documents and only assigns one in case it has no value (`Guid.Empty`
or `0`). A `string` id must be set by you unless you use `UseIdentityKey()`.

::: tip INFO
When using a `Guid`, `int`, or `long` identifier, Marten will ensure the identity is set immediately after calling `IDocumentSession.Store` on the entity.
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

<!-- snippet: sample_identityattribute -->
<a id='snippet-sample_identityattribute'></a>
```cs
public class NonStandardDoc
{
    [Identity]
    public string Name;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/using_natural_identity_keys.cs#L74-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_identityattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The identity property or field can also be configured through `StoreOptions` by using the `Schema` to obtain a document mapping:

<!-- snippet: sample_sample-override-id-fluent-interance -->
<a id='snippet-sample_sample-override-id-fluent-interance'></a>
```cs
storeOptions.Schema.For<OverriddenIdDoc>().Identity(x => x.Name);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/using_natural_identity_keys.cs#L58-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-override-id-fluent-interance' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Guid Identifiers

::: tip INFO
Marten assigns new `Guid` ids with `SequentialGuidIdGeneration`. The ids sort the way PostgreSQL compares a `uuid`, so
inserts go to the end of the index. Since Marten 10 they are not COMB Guids and carry no readable timestamp.
:::

Because `SequentialGuidIdGeneration` is already the default, you only need to configure this explicitly if you want to ensure the strategy is applied uniformly as a policy across all document types:

<!-- snippet: sample_configuring-global-sequentialguid -->
<a id='snippet-sample_configuring-global-sequentialguid'></a>
```cs
options.Policies.ForAllDocuments(m =>
{
    if (m.IdType == typeof(Guid))
    {
        m.IdStrategy = new SequentialGuidIdGeneration();
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CombGuidIdGenerationTests.cs#L71-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-sequentialguid -->
<a id='snippet-sample_configuring-mapping-specific-sequentialguid'></a>
```cs
options.Schema.For<UserWithGuid>().IdStrategy(new SequentialGuidIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/CombGuidIdGenerationTests.cs#L106-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Sequential Identifiers with Hilo

The _Hilo_ sequence generation can be customized with either global defaults or document type-specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

To set different global defaults, use the `StoreOptions.Advanced.HiloSequenceDefaults` property like this sample:

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

Use `ResetHiloSequenceFloor<T>()` to reset the "floor" of the Hilo sequence for a single document type:

<!-- snippet: sample_resethilosequencefloor -->
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

## How HiLo Blocks Are Fetched

::: tip
Fetching the next block ahead of time is new in Marten 10.
:::

A HiLo sequence hands out ids from a block of `MaxLo` numbers that it reserves from the database (the "hi") with one round trip.
The id is still assigned synchronously inside `session.Store()`, so a document's id is set the moment it is stored.

As of Marten 10, the sequence reserves the **next** block in the background, with an asynchronous database call, as soon as it
starts using the current one. When `Store()` exhausts the current block, the block that was fetched ahead of time is swapped in
without a database round trip, so as long as a block lasts longer than one database round trip, storing a document never
blocks a thread on the database. If the background fetch has not completed yet, because the block was used up faster than
that (a small `MaxLo`, or a tight loop storing thousands of documents), `Store()` does not wait for it: it fetches a block
synchronously itself, as every block was fetched before Marten 10. Waiting would block the storing thread, usually a thread-pool
thread, on asynchronous work that itself needs a thread-pool thread to finish, and a burst of storing threads could then stall the
whole process ([#5969](https://github.com/JasperFx/marten/issues/5969)). Since `mt_get_next_hi` became a single atomic increment, the
two fetches cannot collide; each gets a block of its own. A background fetch that fails is logged as a warning and the next block is
simply fetched synchronously; it never surfaces as an exception of its own.

The first id a database issues also makes sure the `mt_hilo` table and the `mt_get_next_hi` function exist and are up to date, as
`AutoCreateSchemaObjects` allows. That check uses synchronous database calls, one statement at a time, so it never waits on the
thread pool either. With `AutoCreate.None` it does not touch the database at all.

What this means for the ids you see:

- Ids are still unique, and the ids one application process issues for a document type are still increasing.
- Each process reserves up to one block more than it has used. Ids in a block that was fetched ahead of time but never used,
  because the process shut down or the floor was reset, are never issued, which leaves a gap of up to `MaxLo` ids.
- Ids are not guaranteed to be contiguous across a block boundary. If the synchronous fallback reserves a block while the
  background fetch is still in flight, and the background fetch turns out to have reserved the lower block, that lower block is
  discarded rather than issued out of order.

HiLo ids were never guaranteed to be gap-free, since several processes share one sequence and a process that stops loses the rest
of its block. Use a database sequence of your own if you need contiguous numbers.

### `MaxAdvanceToNextHiAttempts` {#max-advance-to-next-hi-attempts}

`HiloSettings.MaxAdvanceToNextHiAttempts` (default `30`) is how many times a sequence calls `mt_get_next_hi` for one block
before it gives up with `HiloSequenceAdvanceToNextHiAttemptsExceededException`. It is set with the other HiLo defaults, or
per document type through `HiloSettings()` in the fluent interface (the `[HiloSequence]` attribute does not expose it):

<!-- snippet: sample_docs_hilo_max_advance_to_next_hi_attempts -->
<a id='snippet-sample_docs_hilo_max_advance_to_next_hi_attempts'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // The default is 30
    opts.Advanced.HiloSequenceDefaults.MaxAdvanceToNextHiAttempts = 50;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L205-L215' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_hilo_max_advance_to_next_hi_attempts' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of Marten 10 there is normally nothing to retry. The old `mt_get_next_hi` returned `-1` to a caller that another caller
overtook, and that is what the attempts were spent on. The Marten 10 function is a single atomic increment that always
returns a block ([#5938](https://github.com/JasperFx/marten/issues/5938)), so the first attempt succeeds. The setting only
matters on a database that still has the pre-10 function, before the schema migration that replaces it has been applied.

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

<!-- snippet: sample_documentwithstringid -->
<a id='snippet-sample_documentwithstringid'></a>
```cs
public class DocumentWithStringId
{
    public string Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/IdentityKeyGenerationTests.cs#L32-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_documentwithstringid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can use the "identity key" option for identity generation that would create string values of the pattern `[type alias]/[sequence]` where the type alias is typically the document class name in all lower case and the sequence is a _HiLo_ sequence number.

You can opt into the _identity key_ strategy for identity and even override the document alias name with this syntax:

<!-- snippet: sample_using_identitykey -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/Identity/Sequences/IdentityKeyGenerationTests.cs#L43-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_identitykey' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Identity Strategies

Marten 9 and later do not support custom id generation. Marten picks how to assign ids from the id type alone:

| Id type | How Marten assigns a missing id |
| --- | --- |
| `Guid` | `SequentialGuidIdGeneration` (the default), or random `Guid.NewGuid()` values if you set `IdStrategy(new GuidIdGeneration())` |
| `int` or `long` | A HiLo sequence per document type, configured as shown above |
| `string` | Never. You assign the id, or opt into `UseIdentityKey()` |
| Strong typed identifier | The same rule as the type it wraps, as described below |
| F# single-case union | Never. You assign the id |

Setting `IdStrategy` to your own `IIdGeneration` compiles, but it has no effect on how ids are assigned. Assign the id
yourself before `Store()`, or use a `string` id with `UseIdentityKey()`.

## Strong Typed Identifiers <Badge type="tip" text="7.20" />

::: warning
Marten has several rules about what it accepts as a strong typed identifier. The simplest path is to copy the examples
below and use either Vogen or StronglyTypedId.
:::

::: info
For strong typed identifiers on aggregates built by event projections, see
[aggregate projections](/events/projections/aggregate-projections#strong-typed-identifiers).
:::

Marten can now support [strong typed identifiers](https://en.wikipedia.org/wiki/Strongly_typed_identifier) using a couple different strategies.
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
public readonly struct Task2Id
{
    private Task2Id(Guid value) => Value = value;

    public Guid Value { get; }

    public static Task2Id From(Guid value) => new Task2Id(value);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/TestingTypes.cs#L32-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_valid_strong_typed_identifiers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The type must be a public `struct` with exactly one public property of type `int`, `long`, `Guid` or `string`, plus a
public constructor or public static method that takes that value. (An F# single-case union must also have a name ending
in `Id`.)

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
public readonly partial struct InvoiceId;

public class Invoice
{
    // Marten will use this for the identifier
    // of the Invoice document
    public InvoiceId? Id { get; set; }
    public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/VogenIds/guid_based_document_operations.cs#L317-L330' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_invoice_with_vogen_id' title='Start of snippet'>anchor</a></sup>
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
    theSession.Store(invoice);
    await theSession.SaveChangesAsync();

    // Load by the strong typed identifier
    var loaded = await theSession.LoadAsync<Invoice>(invoice.Id);
    loaded.Name.ShouldBe("updated");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/VogenIds/guid_based_document_operations.cs#L51-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_insert_the_load_by_strong_typed_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
The identity member of a strong typed identifier does not have to be `Nullable<T>`.
:::

The rules and internal behavior are:

- The identity selection is done just the same as the primitive types, Marten is either looking for an `id`/`Id` member, or a member decorated with
  `[Identity]`
- `IQuerySession.LoadAsync<T>(object id)` is the overload for strong typed identifiers
- For `Guid`-wrapped values, Marten is assigning missing identity values based on its sequential `Guid` support
- For `int` or `long`-wrapped values, Marten is using its HiLo support to define the wrapped values
- For `string`-wrapped values, Marten is going to require you to assign the identity to documents yourself

For another example, here's a usage of an `int` wrapped identifier:

<!-- snippet: sample_order2_with_strong_typed_identifier -->
<a id='snippet-sample_order2_with_strong_typed_identifier'></a>
```cs
[StronglyTypedId(Template.Int)]
public readonly partial struct Order2Id;

public class Order2
{
    public Order2Id? Id { get; set; }
    public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/StrongTypedId/int_based_document_operations.cs#L237-L248' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order2_with_strong_typed_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of now, Marten supports:

- Loading a single document by its identifier
- Loading multiple documents using `IsOneOf()` as shown below:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/StrongTypedId/long_based_document_operations.cs#L105-L124' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_strong_typed_identifier_and_is_one_of' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_strong_typed_identifier_and_is_one_of-1'></a>
```cs
[Fact]
public async Task load_many()
{
    var issue1 = new Issue3{Name = Guid.NewGuid().ToString()};
    var Issue3 = new Issue3{Name = Guid.NewGuid().ToString()};
    var issue3 = new Issue3{Name = Guid.NewGuid().ToString()};
    theSession.Store(issue1, Issue3, issue3);

    await theSession.SaveChangesAsync();

    var results = await theSession.Query<Issue3>()
        .Where(x => x.Id.IsOneOf(issue1.Id, Issue3.Id, issue3.Id))
        .ToListAsync();

    results.Count.ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/StrongTypedId/long_based_document_operations.cs#L352-L371' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_strong_typed_identifier_and_is_one_of-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- Deleting a document by identity
- Deleting a document by the document itself
- Within `Include()` queries:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/include_usage.cs#L15-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_include_a_single_reference_with_strong_identifier' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- Within LINQ `Where()` clauses
- Within LINQ `Select()` clauses
- Within LINQ `OrderBy()` clauses
- Identity map resolution
- Automatic dirty checks

`LoadManyAsync()` is not supported for strong typed identifiers.

### LINQ Support {#linq-support}

::: tip
If you want to use custom value types in Marten and especially if you want to involve these value types in LINQ queries,
the Marten team _strongly_ suggests you use either Vogen or StronglyTypedId that are tested today with Marten. There's some
inherent complexity around how these types are serialized to JSON that Marten assumes, and these existing libraries already
handle that for you. We will not officially support custom value types within Marten documents otherwise. 
:::

There's a possible timing issue with the strong typed identifiers. Every time that Marten evaluates the identity strategy
for a document that uses a strong typed identifier, Marten "remembers" that that type is a custom value type and will always
treat any usage of that value type as being the actual wrapped value when constructing any SQL. You _might_ need to 
help out Marten a little bit by telling Marten ahead of time about value types before it tries to evaluate any LINQ 
expressions that use members that are value types like so:

<!-- snippet: sample_limited_doc -->
<a id='snippet-sample_limited_doc'></a>
```cs
[ValueObject<int>]
public readonly partial struct UpperLimit;

[ValueObject<int>]
public readonly partial struct LowerLimit;

public class LimitedDoc
{
    public Guid Id { get; set; }
    public UpperLimit Upper { get; set; }
    public LowerLimit Lower { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L127-L142' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_limited_doc' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_limited_doc-1'></a>
```cs
[ValueObject<long>]
public readonly partial struct UpperLimit;

[ValueObject<int>]
public readonly partial struct LowerLimit;

[ValueObject<string>]
public readonly partial struct Description;

[ValueObject<Guid>]
public readonly partial struct GuidId;

public class LimitedDoc
{
    public Guid Id { get; set; }

    public GuidId? ParentId { get; set; }
    public UpperLimit? Upper { get; set; }
    public LowerLimit Lower { get; set; }
    public Description? Description { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/VogenIds/linq_querying_with_value_types.cs#L217-L241' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_limited_doc-1' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L21-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_value_types' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_registering_value_types-1'></a>
```cs
// opts is a StoreOptions just like you'd have in
// AddMarten() calls
opts.RegisterValueType(typeof(GuidId));
opts.RegisterValueType(typeof(UpperLimit));
opts.RegisterValueType(typeof(LowerLimit));
opts.RegisterValueType(typeof(Description));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/VogenIds/linq_querying_with_value_types.cs#L16-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_value_types-1' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/linq_querying_with_value_types.cs#L32-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_value_type_in_linq' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_using_value_type_in_linq-1'></a>
```cs
[Fact]
public async Task store_several_and_use_in_LINQ_order_by()
{
    var commonParentId = GuidId.From(Guid.NewGuid());
    var doc1 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(1), Upper = UpperLimit.From(20), Description = Description.From("desc1") };
    var doc2 = new LimitedDoc { Lower = LowerLimit.From(5), Upper = UpperLimit.From(25), Description = Description.From("desc3") };
    var doc3 = new LimitedDoc { Lower = LowerLimit.From(4), Upper = UpperLimit.From(15), Description = Description.From("desc2") };
    var doc4 = new LimitedDoc { ParentId = commonParentId, Lower = LowerLimit.From(3), Upper = UpperLimit.From(10), Description = Description.From("desc4") };

    theSession.Store(doc1, doc2, doc3, doc4);
    await theSession.SaveChangesAsync();

    var orderedByIntBased = await theSession
        .Query<LimitedDoc>()
        .OrderBy(x => x.Lower)
        .Select(x => x.Id)
        .ToListAsync();

    orderedByIntBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc3.Id, doc2.Id);

    var orderedByLongBased = await theSession
        .Query<LimitedDoc>()
        .OrderBy(x => x.Upper)
        .Select(x => x.Id)
        .ToListAsync();

    orderedByLongBased.ShouldHaveTheSameElementsAs(doc4.Id, doc3.Id, doc1.Id, doc2.Id);

    var orderedByStringBased = await theSession
        .Query<LimitedDoc>()
        .OrderBy(x => x.Description)
        .Select(x => x.Id)
        .ToListAsync();

    orderedByStringBased.ShouldHaveTheSameElementsAs(doc1.Id, doc3.Id, doc2.Id, doc4.Id);

    var orderedByGuidBased = await theSession
        .Query<LimitedDoc>()
        .OrderBy(x => x.ParentId)
        .Select(x => x.Id)
        .ToListAsync();

    orderedByGuidBased.ShouldHaveTheSameElementsAs(doc1.Id, doc4.Id, doc2.Id, doc3.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/ValueTypeTests/VogenIds/linq_querying_with_value_types.cs#L29-L76' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_value_type_in_linq-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
