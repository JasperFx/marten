# Soft Deletes

You can opt into using "soft deletes" for certain document types. Using this option means that documents are
never actually deleted out of the database. Rather, a `mt_deleted` field is marked as true and a `mt_deleted_at`
field is updated with the transaction timestamp. If a document type is "soft deleted," Marten will automatically filter out
documents marked as _deleted_ unless you explicitly state otherwise in the Linq `Where` clause.

## Configuring a Document Type as Soft Deleted

You can direct Marten to make a document type soft deleted by either marking the class with an attribute:

<!-- snippet: sample_SoftDeletedAttribute -->
<!-- endSnippet -->

Or by using the fluent interface off of `StoreOptions`:

<!-- snippet: sample_soft-delete-configuration-via-fi -->
<!-- endSnippet -->

## Querying a "Soft Deleted" Document Type

By default, Marten quietly filters out documents marked as deleted from Linq queries as demonstrated
in this acceptance test from the Marten codebase:

<!-- snippet: sample_query_soft_deleted_docs -->
<!-- endSnippet -->

The SQL generated for the first call to `Query<User>()` above would be:

```sql
select d.data ->> 'UserName' from public.mt_doc_user as d where mt_deleted = False order by d.data ->> 'UserName'
```

## Fetching All Documents, Deleted or Not

You can include deleted documents with Marten's `MaybeDeleted()` method in a Linq `Where` clause
as shown in this acceptance tests:

<!-- snippet: sample_query_maybe_soft_deleted_docs -->
<!-- endSnippet -->

## Fetching Only Deleted Documents

You can also query for only documents that are marked as deleted with Marten's `IsDeleted()` method
as shown below:

<!-- snippet: sample_query_is_soft_deleted_docs -->
<!-- endSnippet -->

## Fetching Documents Deleted before or after a specific time

To search for documents that have been deleted before a specific time use Marten's `DeletedBefore(DateTimeOffset)` method
and the counterpart `DeletedSince(DateTimeOffset)` as show below:

<!-- snippet: sample_query_soft_deleted_since -->
<!-- endSnippet -->

_Neither `DeletedSince` nor `DeletedBefore` are inclusive searches as shown_
