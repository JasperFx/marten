# PostgreSQL Sequences <Badge type="tip" text="8.x" />

Marten exposes a small helper on `IQuerySession` for fetching the next value of a
PostgreSQL [sequence](https://www.postgresql.org/docs/current/sql-createsequence.html).
It's a thin wrapper around `SELECT nextval(<sequence name>)` that keeps the call
on the session's connection and retry pipeline, so you don't have to drop down to
raw SQL in your application code.

## Why

Sequences are the idiomatic way to generate monotonically-increasing identifiers
in Postgres — human-readable reference numbers, ticket numbers, invoice numbers,
or any other running counter that should not be produced client-side. They're
transactionally safe, gap-tolerant by design (an uncommitted `nextval` still
advances the sequence), and decoupled from any particular table.

A common pattern is to define the sequence through Marten's
[FeatureSchemaBase](/scenarios/using-sequence-for-unique-id) so it's created and
migrated alongside the rest of your schema, then call into it at runtime with
the methods described below.

## Fetching the next value

Given a sequence already created in the database, call `NextSequenceValue` on
any `IQuerySession` (or `IDocumentSession`, which extends it). The name is
passed as a string and can be schema-qualified:

<!-- snippet: sample_next_sequence_value_by_string -->
<a id='snippet-sample_next_sequence_value_by_string'></a>
```cs
await using var session = theStore.QuerySession();

// Fetch the next value of a PostgreSQL sequence by name.
// The name can be schema-qualified (e.g. "my_schema.my_sequence").
var first = await session.NextSequenceValue($"{SchemaName}.seq_int_str");
var second = await session.NextSequenceValue($"{SchemaName}.seq_int_str");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/next_sequence_value_tests.cs#L32-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_next_sequence_value_by_string' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you already have a `DbObjectName` handle — for example, from a Weasel
`Sequence` schema object in one of your custom feature schemas — pass it
directly. Marten uses its `QualifiedName` under the hood:

<!-- snippet: sample_next_sequence_value_by_dbobjectname -->
<a id='snippet-sample_next_sequence_value_by_dbobjectname'></a>
```cs
await using var session = theStore.QuerySession();

// Pass a DbObjectName (here, Weasel's PostgresqlObjectName) when you already have
// a strongly-typed reference to the sequence — for example, from a Weasel Sequence
// schema object built by your own FeatureSchemaBase.
var name = new PostgresqlObjectName(SchemaName, "seq_int_obj");
var first = await session.NextSequenceValue(name);
var second = await session.NextSequenceValue(name);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/next_sequence_value_tests.cs#L55-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_next_sequence_value_by_dbobjectname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Both overloads return `Task<int>`. The value is narrowed from Postgres's native
`bigint` with a SQL-side `::int` cast, which will throw if the sequence has
grown beyond `Int32.MaxValue` (~2.1 billion).

## Long-valued sequences

PostgreSQL sequences are 64-bit by default. For sequences that may exceed the
range of a signed 32-bit integer — or when you simply prefer to work in `long`
from the start — use `NextSequenceValueAsLong`:

<!-- snippet: sample_next_sequence_value_as_long -->
<a id='snippet-sample_next_sequence_value_as_long'></a>
```cs
await using var session = theStore.QuerySession();

// Use NextSequenceValueAsLong when the sequence may exceed Int32.MaxValue
// (roughly 2.1 billion). nextval() is a bigint in Postgres natively.
var first = await session.NextSequenceValueAsLong($"{SchemaName}.seq_big");
var second = await session.NextSequenceValueAsLong(new PostgresqlObjectName(SchemaName, "seq_big"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/next_sequence_value_tests.cs#L107-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_next_sequence_value_as_long' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`NextSequenceValueAsLong` returns the `bigint` produced by `nextval()` as-is,
without any client-side narrowing.

## API summary

```cs
// On IQuerySession
Task<int>  NextSequenceValue(string sequenceName, CancellationToken token = default);
Task<int>  NextSequenceValue(DbObjectName sequenceName, CancellationToken token = default);
Task<long> NextSequenceValueAsLong(string sequenceName, CancellationToken token = default);
Task<long> NextSequenceValueAsLong(DbObjectName sequenceName, CancellationToken token = default);
```

All four overloads share the same implementation: a parameterized
`select nextval(:seq)` (optionally with an `::int` cast for the `Task<int>`
overloads) executed through the session's normal connection lifetime.

## Related

For an end-to-end example that combines sequence definition through
`FeatureSchemaBase` with runtime consumption, see
[Using sequences for unique and human-readable identifiers](/scenarios/using-sequence-for-unique-id).
