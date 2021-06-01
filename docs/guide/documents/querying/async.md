# Asynchronous Querying

## Linq Operators

Marten adds extension methods to `IQueryable` for the asynchronous invocation of the common Linq operators:

* `AnyAsync()`
* `CountAsync()`
* `MinAsync()`
* `MaxAsync()`
* `AverageAsync()`
* `SumAsync()`
* `LongCountAsync()`
* `FirstAsync()/FirstOrDefaultAsync()`
* `SingleAsync()/SingleOrDefaultAsync()`
* `ToListAsync()`

An example usage of `ToListAsync()` is shown below:

<!-- snippet: sample_using-to-list-async -->
<!-- endSnippet -->

## Querying by SQL

To query for results with user-supplied SQL, use:

<!-- snippet: sample_using-queryasync -->
<!-- endSnippet -->
