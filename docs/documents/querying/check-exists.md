# Checking Document Existence

Sometimes you only need to know whether a document with a given id exists in the database, without actually loading and deserializing the full document. Marten provides the `CheckExistsAsync` API for this purpose, which issues a lightweight `SELECT EXISTS(...)` query against PostgreSQL. This avoids the overhead of JSON deserialization and object materialization, making it significantly more efficient than loading the document just to check if it's there.

## Usage

`CheckExistsAsync<T>` is available on `IQuerySession` (and therefore also on `IDocumentSession`). It supports all identity types: `Guid`, `int`, `long`, `string`, and strongly-typed identifiers.

<!-- snippet: sample_check_exists_usage -->
<a id='snippet-sample_check_exists_usage'></a>
```cs
[Fact]
public async Task check_exists_by_object_id()
{
    var doc = new GuidDoc { Id = Guid.NewGuid() };
    theSession.Store(doc);
    await theSession.SaveChangesAsync();

    // Use the object overload for dynamic id types
    var exists = await theSession.CheckExistsAsync<GuidDoc>((object)doc.Id);
    exists.ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/check_document_exists.cs#L89-L103' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_check_exists_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Supported Identity Types

| Id Type | Supported |
| ------- | --------- |
| `Guid` | Yes |
| `int` | Yes |
| `long` | Yes |
| `string` | Yes |
| `object` | Yes (for dynamic id types) |
| Strong-typed ids (Vogen, record structs, etc.) | Yes (via `object` overload) |

## Batched Queries

`CheckExists<T>` is also available as part of [batched queries](/documents/querying/batched-queries), allowing you to check existence of multiple documents in a single round-trip to the database:

<!-- snippet: sample_check_exists_batch_usage -->
<a id='snippet-sample_check_exists_batch_usage'></a>
```cs
[Fact]
public async Task check_exists_in_batch_by_guid_id()
{
    var doc = new GuidDoc { Id = Guid.NewGuid() };
    theSession.Store(doc);
    await theSession.SaveChangesAsync();

    var batch = theSession.CreateBatchQuery();
    var existsHit = batch.CheckExists<GuidDoc>(doc.Id);
    var existsMiss = batch.CheckExists<GuidDoc>(Guid.NewGuid());
    await batch.Execute();

    (await existsHit).ShouldBeTrue();
    (await existsMiss).ShouldBeFalse();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/check_document_exists.cs#L112-L130' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_check_exists_batch_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Behavior Notes

- Returns `true` if the document exists, `false` otherwise.
- Respects soft-delete filters: if a document type uses soft deletes, a soft-deleted document will return `false`.
- Respects multi-tenancy: the check is scoped to the current session's tenant.
- Does **not** load the document into the identity map or trigger any deserialization.
