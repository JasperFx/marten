# Metadata Indexes

The performance of specific queries that include [document and event metadata](/documents/metadata) columns
Marten provides some predefined indexes you may optionally enable

## Last Modified

Should you be using the `ModifiedSince(DateTimeOffset)` or `ModifiedBefore(DateTimeOffset)` you can ask Marten to create
an index on the document's `mt_last_modified` metadata column either using `IndexedLastModifiedAttribute`:

<!-- snippet: sample_index-last-modified-via-attribute -->
<a id='snippet-sample_index-last-modified-via-attribute'></a>
```cs
[IndexedLastModified]
public class Customer
{
    public Guid Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/last_modified_queries.cs#L26-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-last-modified-via-attribute' title='Start of snippet'>anchor</a></sup>
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

## Created At

For `CreatedSince(DateTimeOffset)` or `CreatedBefore(DateTimeOffset)` (see [querying by created time](/documents/metadata#querying-by-created-time)),
Marten can create a btree index on the `mt_created_at` column, with `IndexCreatedAt()` in the fluent interface or the
`[IndexedCreatedAt]` attribute.

The `mt_created_at` column is opt in, and either of these turns it on as well as indexing it.

Using the fluent interface:

<!-- snippet: sample_docs_index_created_at_via_fluent_interface -->
<a id='snippet-sample_docs_index_created_at_via_fluent_interface'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Schema.For<ApiDocsOrder>()
        // IndexCreatedAt() turns the opt-in mt_created_at column on as well
        .IndexCreatedAt();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L189-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_index_created_at_via_fluent_interface' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or the attribute:

<!-- snippet: sample_docs_index_created_at_via_attribute -->
<a id='snippet-sample_docs_index_created_at_via_attribute'></a>
```cs
// [IndexedCreatedAt] turns the opt-in mt_created_at column on as well
[IndexedCreatedAt]
public class ApiDocsAuditEntry
{
    public Guid Id { get; set; }

    [CreatedAtMetadata]
    public DateTimeOffset CreatedAt { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L82-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_index_created_at_via_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`IndexCreatedAt()` takes an optional `Action<DocumentIndex>` to customize the index, like `IndexLastModified()`.

## Tenant Id

When working with multi-tenancy tables, you might wonder if you ever need a single index on the tenantId column, given that it's already the first part of your composite primary key. While the composite key is often sufficient for most queries, there are cases where a dedicated index on just the tenant Id could still be beneficial for performance on specific query's. In that case you can ask Marten to create an index for you on the document's `tenant_id` metadata column either using `IndexedTenantIdAttribute`: 

<!-- snippet: sample_index-tenant-id-via-attribute -->
<a id='snippet-sample_index-tenant-id-via-attribute'></a>
```cs
[IndexedTenantId]
public class TenantIdIndexCustomer
{
    public Guid Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/MartenRegistryTests.cs#L165-L171' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-tenant-id-via-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface:

<!-- snippet: sample_index-tenantid-via-fi -->
<a id='snippet-sample_index-tenantid-via-fi'></a>
```cs
DocumentStore.For(_ =>
{
    _.Schema.For<User>().MultiTenanted();
    _.Schema.For<User>().IndexTenantId();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L32-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-tenantid-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Soft Delete

If using the [soft deletes](/documents/deletes) functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<!-- snippet: sample_softdeletedwithindexattribute -->
<a id='snippet-sample_softdeletedwithindexattribute'></a>
```cs
[SoftDeleted(Indexed = true)]
public class IndexedSoftDeletedDoc
{
    public Guid Id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/configuring_mapping_deletion_style.cs#L45-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_softdeletedwithindexattribute' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/configuring_mapping_deletion_style.cs#L63-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft-delete-with-index-configuration-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This will help Postgres answer queries using `IsDeleted()`, `DeletedSince(DateTimeOffset)` and `DeletedBefore(DateTimeOffset)`
much more efficiently, Postgres will only index documents when they are deleted, `mt_deleted = true`, which also means that the index
does not need to be updated for any insert or update where `mt_deleted = false`
