<!--Title:Metadata Indexes-->

The performance of specific queries that include <[linkto:documentation/schema/metadata;title=Metadata]> columns
Marten provides some predefined indexes you may optionally enable

See also <[linkto:documentation/documents/querying/metadata_queries]>

## Last Modified

Should you be using the `ModifiedSince(DateTimeOffset)` or `ModifiedBefore(DateTimeOffset)` you can ask Marten to create
an index on the document's `mt_last_modified` metadata column either using `IndexedLastModifiedAttribute`:

<[sample:index-last-modified-via-attribute]>

Or by using the fluent interface:

<[sample:index-last-modified-via-fi]>

## Soft Delete

If using the <[linkto:documentation/documents/advanced/soft_deletes;title=Soft Deletes]> functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<[sample:SoftDeletedWithIndexAttribute]>

Or by using the fluent interface:

<[sample:soft-delete-with-index-configuration-via-fi]>

This will help Postgres answer queries using `IsDeleted()`, `DeletedSince(DateTimeOffset)` and `DeletedBefore(DateTimeOffset)`
much more efficiently, Postgres will only index documents when they are deleted, `mt_deleted = true`, which also means that the index
does not need to be updated for any insert or update where `mt_deleted = false`
