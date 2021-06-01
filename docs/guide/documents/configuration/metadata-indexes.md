# Metadata Indexes

The performance of specific queries that include [document and event metadata](/guide/schema/metadata) columns
Marten provides some predefined indexes you may optionally enable

See also [metadata queries](/guide/documents/querying/metadata-queries)

## Last Modified

Should you be using the `ModifiedSince(DateTimeOffset)` or `ModifiedBefore(DateTimeOffset)` you can ask Marten to create
an index on the document's `mt_last_modified` metadata column either using `IndexedLastModifiedAttribute`:

<!-- snippet: sample_index-last-modified-via-attribute -->
<!-- endSnippet -->

Or by using the fluent interface:

<!-- snippet: sample_index-last-modified-via-fi -->
<!-- endSnippet -->

## Soft Delete

If using the [soft deletes](/guide/documents/advanced/soft-deletes) functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<!-- snippet: sample_SoftDeletedWithIndexAttribute -->
<!-- endSnippet -->

Or by using the fluent interface:

<!-- snippet: sample_soft-delete-with-index-configuration-via-fi -->
<!-- endSnippet -->

This will help Postgres answer queries using `IsDeleted()`, `DeletedSince(DateTimeOffset)` and `DeletedBefore(DateTimeOffset)`
much more efficiently, Postgres will only index documents when they are deleted, `mt_deleted = true`, which also means that the index
does not need to be updated for any insert or update where `mt_deleted = false`
