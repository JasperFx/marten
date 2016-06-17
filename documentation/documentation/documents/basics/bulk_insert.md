<!--Title:Bulk Insert-->
<!--Url:bulk_insert-->

Marten supports [Postgresql's COPY](http://www.postgresql.org/docs/9.4/static/sql-copy.html) functionality for very efficient insertion of documents like you might need for test data set up or data migrations. Marten calls this feature "Bulk Insert," and it's exposed off the `IDocumentStore` interface as shown below:

<[sample:using_bulk_insert]>

The bulk insert is done with a single transaction. For really large document collections, you may need to page the calls to `IDocumentStore.BulkInsert()`.

**By default, bulk insert will fail if there are any duplicate id's between the documents being inserted and the existing database data.**

If you want to use the bulk insert feature, but you know that you could have duplicate documents, you can use one of the two following optional modes:

## Ignore Duplicate Values

In this case, you only want to insert brand new documents and just throwaway any potential changes to existing documents.

<[sample:bulk_insert_with_IgnoreDuplicates]>

Internally, Marten creates a temporary table matching the targeted document table and inserts the new values into that table. After writing those documents, Marten issues
an INSERT command to copy rows from the temporary table to the real table, filtering out any matches in existing id's.

## Overwrite Duplicates

In the second case, you want to use the bulk insert to write a batch of documents and overwrite any existing data with matching id's:

<[sample:bulk_insert_with_OverwriteExisting]>

Internally, Marten creates a temporary table matching the targeted document table and inserts the new values into that table. After writing those documents, Marten issues
an INSERT command to copy rows from the temporary table to the real table, filtering out any matches in existing id's, then a second UPDATE command to overwrite data from 
the matching id's.


