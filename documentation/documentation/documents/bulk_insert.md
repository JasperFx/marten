<!--Title:Bulk Insert-->
<!--Url:bulk_insert-->

Marten supports [Postgresql's COPY](http://www.postgresql.org/docs/9.4/static/sql-copy.html) functionality for very efficient insertion of documents like you might need for test data set up or data migrations. Marten calls this feature "Bulk Insert," and it's exposed off the `IDocumentStore` interface as shown below:

<[sample:using_bulk_insert]>

The bulk insert is done with a single transaction. For really large document collections, you may need to page the calls to `IDocumentStore.BulkInsert()`.
