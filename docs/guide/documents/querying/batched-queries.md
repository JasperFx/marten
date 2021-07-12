# Batched Queries

For the sake of performance, if you have a case where you may need to fetch several sets of document data from Marten
at one time, you can opt to batch those queries into a single request to the underlying database to reduce network round trips.

The mechanism for doing this is the `IBatchedQuery` object that you can create with the `IQuerySession.CreateBatchQuery()` method.
The batched queries in Marten work by allowing a user to define the queries they want to run through the batch and getting back
a .Net `Task` that can be used to retrieve the actual results later after the batch is finished. When the batch is executed,
Marten combines all the queries into a single command sent to the underlying Postgresql database, then reads through all
the data returned and sets the results of the `Task` objects handed out earlier.

This functionality is demonstrated below:

<!-- snippet: sample_using-batch-query -->
<a id='snippet-sample_using-batch-query'></a>
```cs
// Start a new IBatchQuery from an active session
var batch = theSession.CreateBatchQuery();

// Fetch a single document by its Id
var user1 = batch.Load<User>("username");

// Fetch multiple documents by their id's
var admins = batch.LoadMany<User>().ById("user2", "user3");

// User-supplied sql
var toms = batch.Query<User>("where first_name == ?", "Tom");

// Where with Linq
var jills = batch.Query<User>().Where(x => x.FirstName == "Jill").ToList();

// Any() queries
var anyBills = batch.Query<User>().Any(x => x.FirstName == "Bill");

// Count() queries
var countJims = batch.Query<User>().Count(x => x.FirstName == "Jim");

// The Batch querying supports First/FirstOrDefault/Single/SingleOrDefault() selectors:
var firstInternal = batch.Query<User>().OrderBy(x => x.LastName).First(x => x.Internal);

// Kick off the batch query
await batch.Execute();

// All of the query mechanisms of the BatchQuery return
// Task's that are completed by the Execute() method above
var internalUser = await firstInternal;
Debug.WriteLine($"The first internal user is {internalUser.FirstName} {internalUser.LastName}");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L543-L575' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-batch-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Combining Compiled Queries and Batch Queries

As of v0.8.10, Marten allows you to incorporate <[linkto:documentation/documents/querying/compiled_queries;title=compiled queries]> as part of a batch query. The Marten team is hoping that this combination will make it easier to create performant applications where you may need to aggregate many documents in a single HTTP request or other operation.

Say you have a compiled query that finds the first user with a given first name:

<!-- snippet: sample_FindByFirstName -->
<a id='snippet-sample_findbyfirstname'></a>
```cs
public class FindByFirstName : ICompiledQuery<User, User>
{
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
    {
        return q => q.FirstOrDefault(x => x.FirstName == FirstName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L134-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_findbyfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use that compiled query class in a batch query, you simply use the `IBatchedQuery.Query(ICompiledQuery)` syntax shown below:

<!-- snippet: sample_batch-query-with-compiled-queries -->
<a id='snippet-sample_batch-query-with-compiled-queries'></a>
```cs
var batch = theSession.CreateBatchQuery();

var justin = batch.Query(new FindByFirstName {FirstName = "Justin"});
var tamba = batch.Query(new FindByFirstName {FirstName = "Tamba"});

await batch.Execute();

(await justin).Id.ShouldBe(user1.Id);
(await tamba).Id.ShouldBe(user2.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L149-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-query-with-compiled-queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Running Synchronously

As of v0.9.1, Marten also exposes the batch querying functionality with a synchronous option:

<!-- snippet: sample_batch-query-with-compiled-queries-synchronously -->
<a id='snippet-sample_batch-query-with-compiled-queries-synchronously'></a>
```cs
var batch = theSession.CreateBatchQuery();

var justin = batch.Query(new FindByFirstName { FirstName = "Justin" });
var tamba = batch.Query(new FindByFirstName { FirstName = "Tamba" });

batch.ExecuteSynchronously();

justin.Result.Id.ShouldBe(user1.Id);
tamba.Result.Id.ShouldBe(user2.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L165-L175' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-query-with-compiled-queries-synchronously' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The mechanics of running synchronously are identical except for calling `IBatchedQuery.ExecuteSynchronously()`.
