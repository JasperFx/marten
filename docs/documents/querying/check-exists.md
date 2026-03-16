# Checking Document Existence

Sometimes you only need to know whether a document with a given id exists in the database, without actually loading and deserializing the full document. Marten provides the `CheckExistsAsync` API for this purpose, which issues a lightweight `SELECT EXISTS(...)` query against PostgreSQL. This avoids the overhead of JSON deserialization and object materialization, making it significantly more efficient than loading the document just to check if it's there.

## Usage

`CheckExistsAsync<T>` is available on `IQuerySession` (and therefore also on `IDocumentSession`). It supports all identity types: `Guid`, `int`, `long`, `string`, and strongly-typed identifiers.

<!-- snippet: sample_check_exists_usage -->
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
<!-- endSnippet -->

## Behavior Notes

- Returns `true` if the document exists, `false` otherwise.
- Respects soft-delete filters: if a document type uses soft deletes, a soft-deleted document will return `false`.
- Respects multi-tenancy: the check is scoped to the current session's tenant.
- Does **not** load the document into the identity map or trigger any deserialization.
