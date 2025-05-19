# Querying Documents with Linq

Marten uses the [Relinq library](https://github.com/re-motion/Relinq) to support a subset of the normal [Linq](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/) operators as well as some Marten specific
operators. Linq queries are done with
Marten using the `IQuerySession.Query<T>()` or `IDocumentSession.Query<T>()` method to return an `IMartenQueryable` object which is in turn
implements the traditional [IQueryable](https://msdn.microsoft.com/en-us/library/system.linq.iqueryable(v=vs.100).aspx) for the document type `T`.

<!-- snippet: sample_querying_with_linq -->
<a id='snippet-sample_querying_with_linq'></a>
```cs
/// <summary>
///     Use Linq operators to query the documents
///     stored in Postgresql
/// </summary>
/// <typeparam name="T"></typeparam>
/// <returns></returns>
IMartenQueryable<T> Query<T>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/IQuerySession.cs#L156-L166' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To query for all documents of a type - not that you would do this very often outside of testing - use the `Query<T>()` method like this:

<!-- snippet: sample_query_for_all -->
<a id='snippet-sample_query_for_all'></a>
```cs
public async Task get_all_documents_of_a_type(IDocumentSession session)
{
    // Calling ToArray() just forces the query to be executed
    var targets = await session.Query<Target>().ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L11-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_all' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At this point, Marten's Linq support has been tested against these .Net types:

1. `String`
1. `Int32` & `Int64` (`int` and `long`)
1. `Decimal` (float)
1. `DateTime` and `DateTimeOffset`
1. `Enum` values
1. `Nullable<T>` of all of the above types
1. `Boolean`
1. `Double`
1. `Float`
