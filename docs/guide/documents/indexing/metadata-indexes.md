# Metadata Indexes

The performance of specific queries that include [document and event metadata](/guide/schema/metadata) columns
Marten provides some predefined indexes you may optionally enable

See also [metadata queries](/guide/documents/querying/metadata-queries)

## Last Modified

Should you be using the `ModifiedSince(DateTimeOffset)` or `ModifiedBefore(DateTimeOffset)` you can ask Marten to create
an index on the document's `mt_last_modified` metadata column either using `IndexedLastModifiedAttribute`:

<!-- snippet: sample_index-last-modified-via-attribute -->
<a id='snippet-sample_index-last-modified-via-attribute'></a>
```cs
[IndexedLastModified]
public class Customer
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_last_modified_index_Tests.cs#L19-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-last-modified-via-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface:

<!-- snippet: sample_index-last-modified-via-fi -->
<a id='snippet-sample_index-last-modified-via-fi'></a>
```cs
DocumentStore.For(_ =>
{
    _.Schema.For<User>().IndexLastModified();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L18-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-last-modified-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Soft Delete

If using the [soft deletes](/guide/documents/advanced/soft-deletes) functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<!-- snippet: sample_SoftDeletedWithIndexAttribute -->
<a id='snippet-sample_softdeletedwithindexattribute'></a>
```cs
[SoftDeleted(Indexed = true)]
public class IndexedSoftDeletedDoc
{
    public Guid Id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#L45-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_softdeletedwithindexattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface:

<!-- snippet: sample_soft-delete-with-index-configuration-via-fi -->
<a id='snippet-sample_soft-delete-with-index-configuration-via-fi'></a>
```cs
DocumentStore.For(_ =>
{
    _.Schema.For<User>().SoftDeletedWithIndex();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#L63-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft-delete-with-index-configuration-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This will help Postgres answer queries using `IsDeleted()`, `DeletedSince(DateTimeOffset)` and `DeletedBefore(DateTimeOffset)`
much more efficiently, Postgres will only index documents when they are deleted, `mt_deleted = true`, which also means that the index
does not need to be updated for any insert or update where `mt_deleted = false`
