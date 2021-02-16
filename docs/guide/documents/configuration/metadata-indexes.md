# Metadata Indexes

The performance of specific queries that include [document and event metadata](/guide/schema/metadata) columns
Marten provides some predefined indexes you may optionally enable

See also [metadata queries](/guide/documents/querying/metadata-queries)

## Last Modified

Should you be using the `ModifiedSince(DateTimeOffset)` or `ModifiedBefore(DateTimeOffset)` you can ask Marten to create
an index on the document's `mt_last_modified` metadata column either using `IndexedLastModifiedAttribute`:

<<< @/../src/Marten.Schema.Testing/configuring_last_modified_index_Tests.cs#sample_index-last-modified-via-attribute

Or by using the fluent interface:

<<< @/../src/Marten.Testing/Examples/MartenRegistryExamples.cs#sample_index-last-modified-via-fi

## Soft Delete

If using the [soft deletes](/guide/documents/advanced/soft-deletes) functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<<< @/../src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#sample_SoftDeletedWithIndexAttribute

Or by using the fluent interface:

<<< @/../src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#sample_soft-delete-with-index-configuration-via-fi

This will help Postgres answer queries using `IsDeleted()`, `DeletedSince(DateTimeOffset)` and `DeletedBefore(DateTimeOffset)`
much more efficiently, Postgres will only index documents when they are deleted, `mt_deleted = true`, which also means that the index
does not need to be updated for any insert or update where `mt_deleted = false`
