<!--Title:Batched Queries-->
<!--Url:batched_queries-->

For the sake of performance, if you have a case where you may need to fetch several sets of document data from Marten
at one time, you can opt to batch those queries into a single request to the underlying database to reduce network round trips.

The mechanism for doing this is the `IBatchedQuery` object that you can create with the `IQuerySession.CreateBatchQuery()` method.
The batched queries in Marten work by allowing a user to define the queries they want to run through the batch and getting back
a .Net `Task` that can be used to retrieve the actual results later after the batch is finished. When the batch is executed,
Marten combines all the queries into a single command sent to the underlying Postgresql database, then reads through all
the data returned and sets the results of the `Task` objects handed out earlier.

This functionality is demonstrated below:

<[sample:using-batch-query]>
