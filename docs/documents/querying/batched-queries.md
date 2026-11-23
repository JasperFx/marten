# Batched Queries

::: tip
You can work with the results of the earliest queries in a batch before the later queries are completely processed, so
your own code can start on them sooner.
:::

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
var batch = session.CreateBatchQuery();

// Fetch a single document by its Id
var user1 = batch.Load<User>("username");

// Fetch multiple documents by their id's
var admins = batch.LoadMany<User>().ById("user2", "user3");

// User-supplied sql
var toms = batch.Query<User>("where data ->> 'FirstName' = ?", "Tom");

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L526-L560' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-batch-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: info These are not the synchronous LINQ operators
`ToList()`, `Any()`, `Count()`, `First()` and the others on `batch.Query<T>()` are Marten's own methods on
`IBatchedQueryable<T>`, and each returns a `Task` that completes when the batch runs. Unlike the `System.Linq`
operators, which throw in Marten 10 because they would run a query synchronously, they are the intended way to add a
LINQ query to a batch.
:::

## What a Batch Can Hold

- Documents by id: `Load<T>()`, `LoadMany<T>().ById()` and `CheckExists<T>()`
- Your own SQL: `Query<T>(sql, parameters)`
- LINQ queries through `Query<T>()`, ending in `ToList()`, `Any()`, `Count()`, `First()`, `FirstOrDefault()`,
  `Single()`, `SingleOrDefault()`, `Min()`, `Max()`, `Sum()` or `Average()`, and including `Select()` transforms,
  `Include()`, paging with `Skip()` and `Take()`, and `Stats()`
- [Compiled queries](#combining-compiled-queries-and-batch-queries) and [query plans](/documents/querying/compiled-queries#query-plans)
  with `QueryByPlan()`
- Event store reads through `batch.Events`: `Load()` for one event, `FetchStream()`, `FetchStreamState()`,
  `EventsExist()`, and the aggregate fetches `FetchLatest<T>()`, `FetchForWriting<T>()` and
  `FetchForExclusiveWriting<T>()`, including by [natural key](/events/natural-keys) and by
  [DCB tags](/events/dcb) with `FetchForWritingByTags<T>()`

Since Marten 10, the aggregate fetches can go anywhere in a batch and share it with each other. A batched fetch of an
`Async` aggregate whose snapshot went backwards between its two reads, as it can while the projection is being rebuilt,
throws `SnapshotRaceException`; retry the batch.

## Combining Compiled Queries and Batch Queries

Marten allows you to incorporate [compiled queries](/documents/querying/compiled-queries) as part of a batch query. The Marten team is hoping that this combination will make it easier to create performant applications where you may need to aggregate many documents in a single HTTP request or other operation.

Say you have a compiled query that finds the first user with a given first name:

<!-- snippet: sample_findbyfirstname -->
<a id='snippet-sample_findbyfirstname'></a>
```cs
public class FindByFirstName: ICompiledQuery<User, User>
{
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
    {
        return q => q.FirstOrDefault(x => x.FirstName == FirstName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L96-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_findbyfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To use that compiled query class in a batch query, you simply use the `IBatchedQuery.Query(ICompiledQuery)` syntax shown below:

<!-- snippet: sample_batch-query-with-compiled-queries -->
<a id='snippet-sample_batch-query-with-compiled-queries'></a>
```cs
var batch = session.CreateBatchQuery();

var justin = batch.Query(new FindByFirstName { FirstName = "Justin" });
var tamba = batch.Query(new FindByFirstName { FirstName = "Tamba" });

await batch.Execute();

(await justin).Id.ShouldBe(user1.Id);
(await tamba).Id.ShouldBe(user2.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L115-L127' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-query-with-compiled-queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
