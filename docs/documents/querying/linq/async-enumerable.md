# Querying to IAsyncEnumerable

::: tip
See [Iterating with Async Enumerables in C# 8](https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8)
for more context around async enumerables
:::

Marten V4.0 introduced a custom Linq operator to return the results of Linq queries to
an `IAsyncEnumerable<T>`. This can be very valuable when you expect large data sets because
it allows you to process the documents being read by Marten in memory **while** Marten
is still fetching additional results and avoids the need to ever put the entire document
result set into memory.

The simple addition to Marten is the `IQueryable<T>.ToAsyncEnumerable()` and `IMartenQueryable<T>`.ToAsyncEnumerable()`
extension methods. Below is a sample usage of this new operator from the Marten tests:

<!-- snippet: sample_query_to_async_enumerable -->
<a id='snippet-sample_query_to_async_enumerable'></a>
```cs
[Fact]
public async Task query_to_async_enumerable()
{
    var targets = Target.GenerateRandomData(20).ToArray();
    await theStore.BulkInsertAsync(targets);

    var ids = new List<Guid>();

    var results = theSession.Query<Target>()
        .ToAsyncEnumerable();

    await foreach (var target in results)
    {
        ids.Add(target.Id);
    }

    ids.Count.ShouldBe(20);
    foreach (var target in targets)
    {
        ids.ShouldContain(target.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/async_enumerable.cs#L17-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_to_async_enumerable' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Be aware not to return the IAsyncEnumerable out of the scope in which the session that produces it is used. This would prevent the database connection from being reused afterwards and thus lead to a connection bleed.
:::
