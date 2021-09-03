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
<a id='snippet-sample_using-to-list-async'></a>
```cs
[Fact]
public async Task use_to_list_async_in_query()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    await theSession.SaveChangesAsync();

    var users = await theSession
        .Query<User>()
        .Where(x => x.FirstName == "Sam")
        .ToListAsync();

    users.Single().FirstName.ShouldBe("Sam");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_queryable_through_to_list_async_Tests.cs#L15-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-to-list-async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying by SQL

To query for results with user-supplied SQL, use:

<!-- snippet: sample_using-queryasync -->
<a id='snippet-sample_using-queryasync'></a>
```cs
var users =
    await
        session.QueryAsync<User>(
            "select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'");
var user = users.Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L312-L320' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-queryasync' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
