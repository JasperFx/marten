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

<!-- snippet: sample_index-tenantId-via-fi -->
<a id='snippet-sample_index-tenantId-via-fi'></a>
```cs
DocumentStore.For(_ =>
{
    _.Schema.For<User>().MultiTenanted();
    _.Schema.For<User>().IndexTenantId();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L32-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_index-tenantId-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Soft Delete

If using the [soft deletes](/documents/deletes) functionality you can ask Marten
to create a partial index on the deleted documents either using `SoftDeletedAttribute`:

<!-- snippet: sample_SoftDeletedWithIndexAttribute -->
<a id='snippet-sample_SoftDeletedWithIndexAttribute'></a>
```cs
[SoftDeleted(Indexed = true)]
public class IndexedSoftDeletedDoc
{
    public Guid Id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/configuring_mapping_deletion_style.cs#L45-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_SoftDeletedWithIndexAttribute' title='Start of snippet'>anchor</a></sup>
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
