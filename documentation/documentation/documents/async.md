<!--Title:Asynchronous Operations-->
<!--Url:async-->

Marten v0.6 added asynchronous alternatives for all querying and persisting methods on the `IQuerySession` and `IDocumentSession` interfaces.
Marten follows the common .Net idiom of suffixing methods with "Async" to denote asynchronous methods. 

## Loading Documents by Id

To load a single document by id asynchronously:

<[sample:persist_and_load_async]>

To load multiple documents by id asynchronously:

<[sample:load_by_id_array_async]>

## Saving Changes

To persist all queued changes in a single unit of work asynchronously, use the `IDocumentSession.SaveChangesAsync()` method:

<[sample:saving-changes-async]>


## Querying by SQL

To query for results with user-supplied SQL, use:

<[sample:using-queryasync]>


## Linq Operators

Marten adds extension methods to `IQueryable` for the asynchronous invocation of the common Linq operators:

* `AnyAsync()`
* `CountAsync()`
* `LongCountAsync()`
* `FirstAsync()/FirstOrDefaultAsync()`
* `SingleAsync()/SingleOrDefaultAsync()`
* `ToListAsync()`

An example usage of `ToListAsync()` is shown below:

<[sample:using-to-list-async]>