# Bulk Insert

Marten supports [Postgresql's COPY](http://www.postgresql.org/docs/9.4/static/sql-copy.html) functionality for very efficient insertion of documents like you might need for test data set up or data migrations. Marten calls this feature "Bulk Insert," and it's exposed off the `IDocumentStore` interface as shown below:

<!-- snippet: sample_using_bulk_insert -->
<a id='snippet-sample_using_bulk_insert'></a>
```cs
// This is just creating some randomized
// document data
var data = Target.GenerateRandomData(100).ToArray();

// Load all of these into a Marten-ized database
theStore.BulkInsert(data, batchSize: 500);

// And just checking that the data is actually there;)
theSession.Query<Target>().Count().ShouldBe(data.Length);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/bulk_loading_Tests.cs#L95-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_bulk_insert' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The bulk insert is done with a single transaction. For really large document collections, you may need to page the calls to `IDocumentStore.BulkInsert()`.

::: warning
By default, bulk insert will fail if there are any duplicate id's between the documents being inserted and the existing database data.
::::

If you want to use the bulk insert feature, but you know that you could have duplicate documents, you can use one of the two following optional modes:

## Ignore Duplicate Values

In this case, you only want to insert brand new documents and just throwaway any potential changes to existing documents.

<!-- snippet: sample_bulk_insert_with_IgnoreDuplicates -->
<a id='snippet-sample_bulk_insert_with_ignoreduplicates'></a>
```cs
var data = Target.GenerateRandomData(100).ToArray();

theStore.BulkInsert(data, BulkInsertMode.IgnoreDuplicates);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/bulk_loading_Tests.cs#L148-L152' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_insert_with_ignoreduplicates' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Internally, Marten creates a temporary table matching the targeted document table and inserts the new values into that table. After writing those documents, Marten issues
an INSERT command to copy rows from the temporary table to the real table, filtering out any matches in existing id's.

## Overwrite Duplicates

In the second case, you want to use the bulk insert to write a batch of documents and overwrite any existing data with matching id's:

<!-- snippet: sample_bulk_insert_with_OverwriteExisting -->
<a id='snippet-sample_bulk_insert_with_overwriteexisting'></a>
```cs
var data = Target.GenerateRandomData(100).ToArray();

theStore.BulkInsert(data, BulkInsertMode.OverwriteExisting);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/bulk_loading_Tests.cs#L168-L172' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_insert_with_overwriteexisting' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Internally, Marten creates a temporary table matching the targeted document table and inserts the new values into that table. After writing those documents, Marten issues
an INSERT command to copy rows from the temporary table to the real table, filtering out any matches in existing id's, then a second UPDATE command to overwrite data from
the matching id's.
