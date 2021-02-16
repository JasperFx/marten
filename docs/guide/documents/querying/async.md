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

<<< @/../src/Marten.Testing/Linq/invoking_queryable_through_to_list_async_Tests.cs#sample_using-to-list-async

## Querying by SQL

To query for results with user-supplied SQL, use:

<<< @/../src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#sample_using-queryasync
