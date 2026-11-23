# Querying Documents with Linq

Marten has its own LINQ provider. It translates a subset of the standard
[LINQ](https://learn.microsoft.com/en-us/dotnet/csharp/linq/) operators, plus some Marten-specific ones, to
PostgreSQL. You start a query with `IQuerySession.Query<T>()` or `IDocumentSession.Query<T>()`. Both return an
`IMartenQueryable<T>`, which implements the standard
[`IQueryable<T>`](https://learn.microsoft.com/en-us/dotnet/api/system.linq.iqueryable-1) for the document type `T`.

<!-- snippet: sample_querying_with_linq -->
<a id='snippet-sample_querying_with_linq'></a>
```cs
/// <summary>
///     Use Linq operators to query the documents
///     stored in Postgresql
/// </summary>
/// <typeparam name="T"></typeparam>
/// <returns></returns>
new IMartenQueryable<T> Query<T>() where T : notnull;
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/IQuerySession.cs#L193-L203' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Every Marten query runs asynchronously. The synchronous `ToList()`, `First()`, `Count()`, `foreach` and the rest still
compile, because they come from `System.Linq`, but they throw `NotSupportedException` naming the asynchronous operator
to use instead. Use `ToListAsync()`, `FirstAsync()`, `CountAsync()` and the other `*Async()` operators, or
[`ToAsyncEnumerable()`](/documents/querying/linq/async-enumerable) to stream the results. See the
[migration guide](/migration-guide#synchronous-data-access-is-gone-in-10-0-0).
:::

## What Marten Supports {#what-marten-supports}

The tables below list the LINQ operators and expression shapes Marten translates to SQL. Each row links to an
example. Marten does not run part of a query in memory to make up for a shape it cannot translate. It throws, and the
last table says which exception you get and what to do instead.

### Filtering with Where() {#supported-filtering}

| Shape | Example |
| --- | --- |
| `==`, `!=`, `>`, `>=`, `<`, `<=`, `&&`, `\|\|` and `!` on members, including members of members (`x.Inner.Number`) and comparisons between two members | [Filtering Documents](/documents/querying/linq/operators#filtering-documents) |
| Arithmetic and `%` inside a predicate | [Modulo Queries](/documents/querying/linq/operators#modulo-queries) |
| Boolean members: `x.Flag`, `!x.Flag`, `x.Flag == false` | [Searching with Boolean Flags](/documents/querying/linq/booleans) |
| `== null`, `!= null` and `HasValue` | [Searching for NULL Values](/documents/querying/linq/nulls) |
| Captured variables, including the optional filter `!captured.HasValue \|\| x.Id == captured` | [Optional Filters](/documents/querying/linq/nulls#optional-filters) |
| A local array, `List<T>` or `HashSet<T>`: `ids.Contains(x.Id)`, a SQL `IN` | [Contains() over a Local Collection](/documents/querying/linq/operators#contains-over-a-local-collection) |
| `IsOneOf()` and `In()` | [IsOneOf](/documents/querying/linq/child-collections#isoneof) |
| `CompareTo()` compared with 0, on numbers, dates and `Guid` | [CompareTo()](/documents/querying/linq/operators#compareto) |
| Raw SQL with `MatchesSql()` and `MatchesJsonPath()` | [Mixing Raw SQL with Linq](/documents/querying/linq/sql) |
| Full-text search: `Search()`, `PlainTextSearch()`, `PhraseSearch()`, `WebStyleSearch()`, `NgramSearch()` | [Text Search](/documents/full-text#text-search) |
| Soft-deleted documents: `MaybeDeleted()` and the other soft-delete filters | [Querying a Soft Deleted Document Type](/documents/deletes#querying-a-soft-deleted-document-type) |
| Tenancy: `AnyTenant()` and `TenantIsOneOf()` | [Querying Multi-Tenanted Documents](/documents/multi-tenancy#querying-multi-tenanted-documents) |
| C# 15 union members on .NET 11: `x.Payment.Value is Card`, `((Card)x.Payment.Value).Last4` | [Querying C# Union Members](/documents/querying/linq/unions) |

### Strings {#supported-strings}

| Shape | Example |
| --- | --- |
| `StartsWith()`, `EndsWith()`, `Contains()` and `Equals()` | [Substring Searches](/documents/querying/linq/strings#substring-searches) |
| The same with `StringComparison.OrdinalIgnoreCase`, and `EqualsIgnoreCase()` | [Case-Insensitive Searches](/documents/querying/linq/strings#case-insensitive-searches) |
| `string.IsNullOrEmpty()` and `string.IsNullOrWhiteSpace()` | [Null, Empty and Whitespace](/documents/querying/linq/strings#null-empty-and-whitespace) |
| `ToLower()`, `ToUpper()`, their `Invariant` forms, `Trim()` and `Length` | [Case, Trim and Length](/documents/querying/linq/strings#case-trim-and-length) |
| `string.Compare()` and `CompareTo()` compared with 0 | [Comparing Strings](/documents/querying/linq/strings#comparing-strings) |
| `Regex.IsMatch()`, with `RegexOptions.IgnoreCase` | [Regular Expressions](/documents/querying/linq/strings#regular-expressions) |

### Collections and Child Collections {#supported-collections}

| Shape | Example |
| --- | --- |
| `Any()`, `Any(predicate)`, `Contains(value)`, `!Contains(value)` and `All(predicate)` | [Quantifier Operations](/documents/querying/linq/child-collections#quantifier-operations) |
| `Any()` inside `Any()` | [Nested Any()](/documents/querying/linq/child-collections#nested-any) |
| `Count()`, `Count(predicate)` and `Length` compared with a number | [Querying within Value IEnumerables](/documents/querying/linq/child-collections#querying-within-value-ienumerables) |
| `Sum()`, `Min()`, `Max()` and `Average()` of a child collection inside a predicate | [How Marten Translates Collection Predicates](/documents/querying/linq/child-collections#how-marten-translates-collection-predicates) |
| An element by position: `x.Values[1]`, `x.Grid[1][0]`, `ElementAt(1)` | [Reading an Element by Position](/documents/querying/linq/child-collections#reading-an-element-by-position) |
| Dictionaries: `ContainsKey()`, `Keys`, `Values`, `Count` and the indexer `x.Tags["color"]` | [Querying Dictionaries](/documents/querying/linq/child-collections#querying-dictionaries) |
| `IsSupersetOf()` and `IsSubsetOf()` | [IsSupersetOf](/documents/querying/linq/child-collections#issupersetof) |

### Projections with Select() {#supported-select}

| Shape | Example |
| --- | --- |
| A member, or a member of a member | [Select()](/documents/querying/linq/projections#select) |
| An anonymous type, a DTO or a record built from members, run as `jsonb_build_object()` | [Server-Side JSON Projections](/documents/querying/linq/projections#server-side-json-projections) |
| `Count()` and `Count(predicate)` of a child collection inside an object projection | [Counting a Child Collection](/documents/querying/linq/projections#counting-a-child-collection) |
| A computed value: arithmetic, `%`, widening casts, string concatenation, `ToUpper()`, `ToLower()`, `Length`, `ToString()` of an integer or a `Guid` | [Computed Values in Select()](/documents/querying/linq/projections#computed-values-in-select) |
| `Where()`, `OrderBy()`, `Distinct()`, paging and aggregates after a `Select()` | [Operators after Select()](/documents/querying/linq/projections#operators-after-select) |
| An object projection that contains anything else, run on the client | [Projections That Can't Be Expressed in SQL](/documents/querying/linq/projections#client-side-projections) |
| Raw SQL in a projection with `ExplicitSql<T>()` | [Raw SQL in Select()](/documents/querying/linq/projections#explicit-sql-in-select) |

### SelectMany() {#supported-selectmany}

| Shape | Example |
| --- | --- |
| Flatten a collection of values or of child documents | [SelectMany()](/documents/querying/linq/projections#selectmany) |
| `Where()`, `Select()`, `OrderBy()`, `Distinct()`, paging, `First()`, `Single()`, `Count()` and aggregates after it | [Operators after SelectMany()](/documents/querying/linq/projections#operators-after-selectmany) |
| `Take()` and `Skip()` before it page the documents; after it they page the flattened rows | [Paging and SelectMany()](/documents/querying/linq/projections#paging-and-selectmany) |

### Ordering and Paging {#supported-ordering}

| Shape | Example |
| --- | --- |
| `OrderBy()`, `OrderByDescending()`, `ThenBy()` and `ThenByDescending()` | [Ordering Results](/documents/querying/linq/operators#ordering-results) |
| A computed key, `OrderBy(x => -x.Number)`, or an element by position | [Computed Ordering Keys](/documents/querying/linq/operators#computed-ordering-keys) |
| Case-insensitive ordering with `StringComparer.OrdinalIgnoreCase` | [Case-insensitive Ordering](/documents/querying/linq/operators#case-insensitive-ordering-for-strings) |
| Ordering by member name: `OrderBy("Date")` | [Ordering with Dynamic Properties](/documents/querying/linq/operators#ordering-with-dynamic-properties) |
| Raw SQL with `OrderBySql()` and `ThenBySql()` | [Ordering with Raw SQL](/documents/querying/linq/operators#ordering-with-raw-sql) |
| `Take()` and `Skip()`; `CountAsync()` after them counts the page | [Partitioning Operators](/documents/querying/linq/operators#partitioning-operators) |
| `ToPagedListAsync()` and `Stats()` | [Paging](/documents/querying/linq/paging#paged-lists) |
| Keyset paging with `ToJsonPageByCursorAsync()` | [Keyset (Cursor) Pagination](/documents/querying/linq/paging#keyset-cursor-pagination) |

### Results, Aggregates and Element Operators {#supported-results}

| Shape | Example |
| --- | --- |
| `ToListAsync()` | [Querying for All Documents](#querying-for-all-documents) |
| `ToAsyncEnumerable()` | [Querying to IAsyncEnumerable](/documents/querying/linq/async-enumerable) |
| `FirstAsync()`, `FirstOrDefaultAsync()`, `SingleAsync()` and `SingleOrDefaultAsync()` | [Element Operations](/documents/querying/linq/operators#element-operations) |
| `AnyAsync()`, `CountAsync()`, `LongCountAsync()`, `MinAsync()`, `MaxAsync()`, `SumAsync()` and `AverageAsync()` | [Aggregate Functions](/documents/querying/linq/operators#aggregate-functions) |
| `Distinct()` | [Distinct()](/documents/querying/linq/operators#distinct) |
| `DistinctBy()` after a `Select()`, as `DISTINCT ON` | [DistinctBy()](/documents/querying/linq/operators#distinctby) |

### Grouping and Joins {#supported-grouping-and-joins}

| Shape | Example |
| --- | --- |
| `GroupBy()` on one or more keys, with `Count()`, `Sum()`, `Min()`, `Max()` and `Average()` | [GroupBy()](/documents/querying/linq/operators#groupby) |
| `Where()` after `GroupBy()`, as `HAVING` | [HAVING](/documents/querying/linq/operators#having-where-after-groupby) |
| Ordering, paging and counting groups | [Ordering and Paging Groups](/documents/querying/linq/operators#ordering-and-paging-groups) |
| `GroupJoin()` followed by `SelectMany()`, as an inner or left join | [Inner Join](/documents/querying/linq/group-join#inner-join) |
| `Join()`, `LeftJoin()` and `RightJoin()` | [Join, LeftJoin, RightJoin and FullJoin](/documents/querying/linq/group-join#join-leftjoin-rightjoin-and-fulljoin) |
| `Count()`, `Distinct()`, `Sum()` and `Max()` over a join | [With Aggregation](/documents/querying/linq/group-join#with-aggregation) |
| `GroupBy()` and computed `OrderBy()` keys over a join | [Grouping and Ordering over a Join](/documents/querying/linq/group-join#grouping-and-ordering-over-a-join) |

### Related Documents {#supported-includes}

| Shape | Example |
| --- | --- |
| `Include()` one related document | [Include a Single Document](/documents/querying/linq/include#include-a-single-document) |
| `Include()` into a list or a dictionary, including from an array of ids | [Include Many Documents](/documents/querying/linq/include#include-many-documents) |
| `Include()` with a filter on the included documents | [Filtering Included Documents](/documents/querying/linq/include#filtering-included-documents) |
| `Include()` on any member, including the children of the queried document | [Mapping to Documents on Any Property](/documents/querying/linq/include#mapping-to-documents-on-any-property) |

### Not Supported {#not-supported}

| Shape | What happens | Do this instead |
| --- | --- | --- |
| Synchronous execution: `ToList()`, `ToArray()`, `foreach`, `First()`, `Single()`, `Count()`, `Any()`, `Sum()`, and anything else that enumerates the query, such as .NET 9's `CountBy()` | `NotSupportedException` naming the asynchronous operator | `ToListAsync()`, `FirstAsync()` and the other `*Async()` operators, or `ToAsyncEnumerable()` |
| `ToAsyncEnumerable()` in a file without `using Marten;` | Binds to .NET 10's `AsyncEnumerable.ToAsyncEnumerable()`, which enumerates the query synchronously: `NotSupportedException` | Add `using Marten;`. See [Querying to IAsyncEnumerable](/documents/querying/linq/async-enumerable#using-marten) |
| `ToPagedList()` | `NotSupportedException` | `ToPagedListAsync()` |
| `Last()` and `LastOrDefault()` | `NotSupportedException`. There is no asynchronous version | Reverse the ordering and use `FirstAsync()` or `FirstOrDefaultAsync()` |
| `ElementAt()` and `ElementAtOrDefault()` on the query itself | `NotSupportedException` | `Skip(index)`, then `FirstAsync()` or `FirstOrDefaultAsync()` |
| `Aggregate()` | `NotSupportedException` | `SumAsync()` and the other aggregates, or load the rows with `ToListAsync()` and aggregate in memory |
| `Reverse()`, `Concat()`, `Union()`, `Zip()`, `TakeWhile()`, `SkipWhile()` and any other operator not listed on this page | `BadLinqExpressionException` | `MatchesSql()`, `session.QueryAsync<T>(sql)`, or load the rows and apply the operator in memory |
| A method call in `Where()` that Marten has no translation for, such as `x.Name.IsNormalized()` | `NotSupportedException` | `MatchesSql()`, or [add your own translation](/documents/querying/linq/extending) |
| A computed `Select()` or `OrderBy()` value that PostgreSQL would not compute the way .NET does: a `Math` call, `? :`, `Substring()`, decimal division, a narrowing cast, floating point `%` | `BadLinqExpressionException`. Marten does not fall back to the client for a scalar `Select()` | Select the members and compute in memory, or project into an object, which [runs on the client](/documents/querying/linq/projections#client-side-projections) |
| `OrderBy()` after a `Select()` into an anonymous type, a DTO or another document | `BadLinqExpressionException` | Order before the `Select()` |
| After `SelectMany()`, a `Select()` into an object followed by `Where()`, `OrderBy()`, `Distinct()` or an aggregate, or a `GroupBy()` of the flattened rows | `BadLinqExpressionException` | Apply the operator before the `Select()` |
| `DistinctBy()` directly on the documents | `BadLinqExpressionException` | `Select()` first, then `DistinctBy()` |
| A negative index, `x.Values[-1]` | `BadLinqExpressionException` | Index from the start of the array |
| `CompareTo()` compared with anything other than 0 | `BadLinqExpressionException` | Compare the result with 0 |
| `GroupJoin()` as the last operator | `NotSupportedException` | Follow it with `SelectMany()` |
| `FullJoin()`, and the join shapes listed under [Join, LeftJoin, RightJoin and FullJoin](/documents/querying/linq/group-join#join-leftjoin-rightjoin-and-fulljoin) | `BadLinqExpressionException` | Query each side and combine in memory, or use `session.QueryAsync<T>(sql)` |
| `ToJsonArray()`, `StreamJsonArray()` and the other raw JSON methods over a projection that runs on the client | `BadLinqExpressionException` | `ToListAsync()` |
| Union shapes Marten cannot tell apart in the stored JSON | `BadLinqExpressionException` | See [What Marten Refuses](/documents/querying/linq/unions#what-marten-refuses) |

## Querying for All Documents {#querying-for-all-documents}

To query for all documents of a type - not that you would do this very often outside of testing - use the `Query<T>()`
method like this:

<!-- snippet: sample_query_for_all -->
<a id='snippet-sample_query_for_all'></a>
```cs
public async Task get_all_documents_of_a_type(IDocumentSession session)
{
    // ToListAsync() executes the query
    var targets = await session.Query<Target>().ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L11-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_all' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Supported .NET Types {#supported-types}

Marten's Linq support has been tested against these .NET types:

1. `String`
1. `Int32` and `Int64` (`int` and `long`)
1. `Decimal`, `Double` and `Single` (`float`)
1. `Boolean`
1. `Guid`
1. `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and `TimeSpan`
1. `Enum` values, stored as integers or as strings
1. `Nullable<T>` of all of the above types
1. Strong-typed identifiers and other value types. See [LINQ Support](/documents/identity#linq-support) for
   strong-typed identifiers
1. C# 15 unions on .NET 11. See [Querying C# Union Members](/documents/querying/linq/unions)

For full-text search, see [Full Text Searching](/documents/full-text).
