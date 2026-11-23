# Querying to IAsyncEnumerable

::: tip
See [Iterating with Async Enumerables in C# 8](https://learn.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8)
for more context around async enumerables.
:::

`ToAsyncEnumerable()` returns the results of a Linq query as an `IAsyncEnumerable<T>`. You process each document while
Marten is still reading the rest, so the whole result set never has to be in memory at once. That is valuable when
you expect a large result.

In Marten 10, `await foreach` over `ToAsyncEnumerable()` is also the replacement for a plain `foreach` over a query,
which throws `NotSupportedException`. See the
[migration guide](/migration-guide#synchronous-data-access-is-gone-in-10-0-0).

Marten adds one extension method, `ToAsyncEnumerable<T>(this IQueryable<T>, CancellationToken token = default)`, in
the `Marten` namespace. Below is a sample usage from the Marten tests:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/async_enumerable.cs#L23-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_to_async_enumerable' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Make Sure Marten's Method Binds {#using-marten}

.NET 10 adds its own `ToAsyncEnumerable()` to the base class library:
`System.Linq.AsyncEnumerable.ToAsyncEnumerable<T>(this IEnumerable<T>)`. It wraps any `IEnumerable<T>`, and it
enumerates the query synchronously, so Marten throws `NotSupportedException` as soon as you iterate it.

Which method the compiler picks depends on the `using` directives in your file:

- With `using Marten;` and `using System.Linq;` at the same level, Marten's method binds, because `IQueryable<T>` is a
  closer match than `IEnumerable<T>`. Global usings and the usings at the top of the file are the same level.
- Without `using Marten;`, the .NET method binds.
- With `using Marten;` at the top of the file but `using System.Linq;` inside a `namespace { }` block, the .NET method
  binds, because the compiler looks for extension methods in the innermost scope first.

So add `using Marten;` to any file that calls `ToAsyncEnumerable()` on a Marten query, next to `using System.Linq;`.

::: warning
Be aware not to return the IAsyncEnumerable out of the scope in which the session that produces it is used. This would prevent the database connection from being reused afterwards and thus lead to a connection bleed.
:::
