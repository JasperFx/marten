# Supported Linq Operators

## Element Operations

Marten supports these [element operations](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/element-operations),
asynchronously only:

1. `FirstAsync()`
1. `SingleAsync()`
1. `FirstOrDefaultAsync()`
1. `SingleOrDefaultAsync()`

::: warning
Synchronous data access is not supported in Marten 10. The synchronous `First()`, `Single()`, `Count()`, `ToList()` and
the rest still compile, because they come from `System.Linq`, but they throw a `NotSupportedException` naming the
asynchronous operator to use. See the [migration guide](/migration-guide#synchronous-data-access-is-gone-in-10-0-0).
:::

<!-- snippet: sample_select_a_single_value -->
<a id='snippet-sample_select_a_single_value'></a>
```cs
public async Task select_a_single_value(IDocumentSession session)
{
    // SingleAsync()/SingleOrDefaultAsync() will throw exceptions if more than
    // one result is returned from the database
    await session.Query<Target>().Where(x => x.Number == 5).SingleAsync();
    await session.Query<Target>().Where(x => x.Number == 5).SingleOrDefaultAsync();

    await session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).FirstAsync();
    await session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).FirstOrDefaultAsync();

    // Marten does not support Last()/LastOrDefault(). Reverse the ordering
    // and use FirstAsync()/FirstOrDefaultAsync() instead.

    // Using the query predicate inside of SingleAsync/FirstAsync is supported as well
    await session.Query<Target>().SingleAsync(x => x.Number == 5);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L152-L170' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_select_a_single_value' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Filtering Documents

Since you usually don't want to pull down the entire database at one time, Marten supports these basic operators in Linq searches:

<!-- snippet: sample_query_by_basic_operators -->
<a id='snippet-sample_query_by_basic_operators'></a>
```cs
public async Task basic_operators(IDocumentSession session)
{
    // Field equals a value
    await session.Query<Target>().Where(x => x.Number == 5).ToListAsync();

    // Field does not equal a value
    await session.Query<Target>().Where(x => x.Number != 5).ToListAsync();

    // Field compared to values
    await session.Query<Target>().Where(x => x.Number > 5).ToListAsync();
    await session.Query<Target>().Where(x => x.Number >= 5).ToListAsync();
    await session.Query<Target>().Where(x => x.Number < 5).ToListAsync();
    await session.Query<Target>().Where(x => x.Number <= 5).ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L34-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_basic_operators' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten's Linq support will also allow you to make "deep" searches on properties of properties (or fields):

<!-- snippet: sample_deep_nested_properties -->
<a id='snippet-sample_deep_nested_properties'></a>
```cs
public void deep_queries(IDocumentSession session)
{
    session.Query<Target>().Where(x => x.Inner.Number == 3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L64-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_nested_properties' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Right now, Marten supports both _and_ and _or_ queries with Linq:

<!-- snippet: sample_querying_with_and_or_or -->
<a id='snippet-sample_querying_with_and_or_or'></a>
```cs
public void and_or(IDocumentSession session)
{
    // AND queries
    session.Query<Target>().Where(x => x.Number > 0 && x.Number <= 5);

    // OR queries
    session.Query<Target>().Where(x => x.Number == 5 || x.Date == DateTime.Today);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L52-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_and_or_or' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Ordering Results

Marten contains support for expressing ordering in both ascending and descending order in Linq queries:

<!-- snippet: sample_ordering-in-linq -->
<a id='snippet-sample_ordering-in-linq'></a>
```cs
public void order_by(IDocumentSession session)
{
    // Sort in ascending order
    session.Query<Target>().OrderBy(x => x.Date);

    // Sort in descending order
    session.Query<Target>().OrderByDescending(x => x.Date);

    // You can use multiple order by's
    session.Query<Target>().OrderBy(x => x.Date).ThenBy(x => x.Number);

    // If you're brave, you can even use raw SQL literals as of Marten v7!
    session.Query<Target>().OrderBySql("substring(d.data -> 'String', 1, 2)");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L99-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ordering-in-linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A key can also read an element of an array or list on the document by position, such as
`OrderBy(x => x.Scores[1])` or `ThenBy(x => x.Grid[1][0])`. See
[Reading an Element by Position](/documents/querying/linq/child-collections#reading-an-element-by-position).

A key that reads nothing from the document, such as a static member, a captured variable or a literal
(`OrderBy(x => Settings.SortKey)`), is the same value for every document. As in LINQ to Objects, it leaves the order
to the other keys, so Marten leaves it out of the `ORDER BY`. `OrderBy(x => Settings.SortKey).ThenBy(x => x.Number)`
orders by `Number`, and a query whose keys are all like that has no `ORDER BY` at all.

A second `OrderBy()` or `OrderByDescending()` re-sorts the results, as in LINQ to Objects. The sort is stable, so the
earlier ordering only breaks ties: `OrderBy(x => x.Category).OrderBy(x => x.Name)` orders by `Name`, then
`Category`. Use `ThenBy()` to add a key after the ones before it.

A key can also be computed from the document's members: `OrderBy(x => -x.Number)`, `OrderBy(x => x.Number % 3)`,
`ThenBy(x => x.Price * x.Quantity)`, `OrderBy(x => x.FirstName + x.LastName)` or `OrderBy(x => x.Name.ToUpper())`.
Marten computes it in SQL with the same translation it uses for a computed `Select()`: member access, arithmetic,
widening casts, string concatenation, `ToUpper()`, `ToLower()`, `Length`, and `ToString()` of an integer or a
`Guid`. A key PostgreSQL would not compute the way .NET does, such as decimal division, floating point `%`, a
narrowing cast, a conditional or a `Math` call, throws `BadLinqExpressionException` rather than ordering by
something else.

## Ordering with dynamic properties

Marten provides helper methods to express ordering using dynamic properties in LINQ queries. This is quite useful for cases where you wouldn't know the properties being used for ordering at build time. This functionality is added in v5.

<!-- snippet: sample_ordering-in-linq-using-dynamic-props -->
<a id='snippet-sample_ordering-in-linq-using-dynamic-props'></a>
```cs
public void order_by_dynamic_props(IDocumentSession session)
{
    // Sort in ascending order
    session.Query<Target>().OrderBy("Date");

    // Sort in descending order
    session.Query<Target>().OrderByDescending("Date");

    // You can use multiple order by's
    session.Query<Target>().OrderBy("Date").ThenBy("Number");
    session.Query<Target>().OrderByDescending("Date").ThenBy("Number");
    session.Query<Target>().OrderBy("Date").ThenByDescending("Number");

    // You can use pass props with sort order text
    session.Query<Target>().OrderBy("Date ASC");
    session.Query<Target>().OrderBy("Date asc");
    session.Query<Target>().OrderBy("Number DESC");
    session.Query<Target>().OrderBy("Number desc");

    // You can use multiple order by props as params or list
    session.Query<Target>().OrderBy("Date DESC", "Number");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L117-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ordering-in-linq-using-dynamic-props' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Case-insensitive ordering for strings

If you use `StringComparer.InvariantCultureIgnoreCase` or `StringComparer.OrdinalIgnoreCase` with an `OrderBy` on strings, Marten automatically applies case-insensitive ordering using `lower()` in generated SQL. This functionality is added in v5.

```cs
// invariant culture ignore case
var query = theSession.Query<Target>().OrderBy(x => x.String, StringComparer.InvariantCultureIgnoreCase);

// ordinal ignore case
var query = theSession.Query<Target>().OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase);
```

## Aggregate Functions

::: info
In many cases the asynchronous versions of these operators are extension methods within Marten itself as these were not present
in core `IQueryable` at the time Marten's Linq support was developed.
:::

Marten supports these [aggregation operators](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/aggregation-operations):

1. `CountAsync()`
1. `LongCountAsync()`
1. `MinAsync()`
1. `MaxAsync()`
1. `SumAsync()`
1. `AverageAsync()`

<!-- snippet: sample_aggregation_operations -->
<a id='snippet-sample_aggregation_operations'></a>
```cs
public async Task sample_aggregation_operations(IQuerySession session)
{
    var count = await session.Query<Target>().CountAsync();
    var count2 = await session.Query<Target>().CountAsync(x => x.Number > 5);
    var count3 = await session.Query<Target>().LongCountAsync();
    var count4 = await session.Query<Target>().LongCountAsync(x => x.Number > 5);

    var min = await session.Query<Target>().MinAsync(x => x.Number);
    var max = await session.Query<Target>().MaxAsync(x => x.Number);
    var sum = await session.Query<Target>().SumAsync(x => x.Number);
    var average = await session.Query<Target>().AverageAsync(x => x.Number);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L204-L219' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregation_operations' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Partitioning Operators

Marten has been successfully tested with these [partition operators](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/partitioning-data):

1. `Take()`
1. `Skip()`

<!-- snippet: sample_using_take_and_skip -->
<a id='snippet-sample_using_take_and_skip'></a>
```cs
public async Task using_take_and_skip(IDocumentSession session)
{
    // gets records 11-20 from the database
    await session.Query<Target>().Skip(10).Take(10).OrderBy(x => x.Number).ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L143-L150' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_take_and_skip' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

TODO -- link to the paging support

## GroupBy()

Marten supports the `GroupBy()` LINQ operator for grouping documents by one or more keys and computing aggregate values. GroupBy translates to SQL `GROUP BY` with aggregate functions like `COUNT`, `SUM`, `MIN`, `MAX`, and `AVG`.

### Simple Key with Aggregates

<!-- snippet: sample_group_by_simple_key_with_count -->
<a id='snippet-sample_group_by_simple_key_with_count'></a>
```cs
[Fact]
public async Task group_by_simple_key_with_count()
{
    await SetupTargetData();

    var results = await _session.Query<Target>()
        .GroupBy(x => x.Color)
        .Select(g => new { Color = g.Key, Count = g.Count() })
        .ToListAsync();

    results.Count.ShouldBe(3);
    results.Single(x => x.Color == Colors.Blue).Count.ShouldBe(2);
    results.Single(x => x.Color == Colors.Green).Count.ShouldBe(3);
    results.Single(x => x.Color == Colors.Red).Count.ShouldBe(1);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_by_operator.cs#L42-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_by_simple_key_with_count' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Composite Key

You can group by multiple properties using an anonymous type:

```csharp
var results = await session.Query<Target>()
    .GroupBy(x => new { x.Color, x.String })
    .Select(g => new { Color = g.Key.Color, Text = g.Key.String, Count = g.Count() })
    .ToListAsync();
```

### Where Before GroupBy

Filter documents before grouping with a standard `Where()` clause:

```csharp
var results = await session.Query<Target>()
    .Where(x => x.Number > 20)
    .GroupBy(x => x.Color)
    .Select(g => new { Color = g.Key, Count = g.Count() })
    .ToListAsync();
```

### HAVING (Where After GroupBy)

Filter groups with a `Where()` clause after `GroupBy()` -- this translates to SQL `HAVING`:

```csharp
var results = await session.Query<Target>()
    .GroupBy(x => x.Color)
    .Where(g => g.Count() > 1)
    .Select(g => new { Color = g.Key, Count = g.Count() })
    .ToListAsync();
```

### Supported Aggregates

The following aggregate methods are supported within GroupBy projections:

- `g.Count()` / `g.LongCount()` -- `COUNT(*)`
- `g.Count(predicate)` / `g.LongCount(predicate)` -- `COUNT(*) FILTER (WHERE predicate)`
- `g.Sum(x => x.Property)` -- `SUM(property)`
- `g.Min(x => x.Property)` -- `MIN(property)`
- `g.Max(x => x.Property)` -- `MAX(property)`
- `g.Average(x => x.Property)` -- `AVG(property)`

### Predicate Counts

`Count(predicate)` and `LongCount(predicate)` use the same predicate translation as
document `Where()` clauses, including nullable comparisons, duplicated fields, compound
predicates, and bound parameters.

## Distinct()

New in Marten 1.2 is support for the Linq `Distinct()` operator:

<!-- snippet: sample_get_distinct_strings -->
<a id='snippet-sample_get_distinct_strings'></a>
```cs
[Fact]
public async Task get_distinct_string()
{
    theSession.Store(new Target {String = "one"});
    theSession.Store(new Target {String = "one"});
    theSession.Store(new Target {String = "two"});
    theSession.Store(new Target {String = "two"});
    theSession.Store(new Target {String = "three"});
    theSession.Store(new Target {String = "three"});

    await theSession.SaveChangesAsync();

    var queryable = theSession.Query<Target>().Select(x => x.String).Distinct();

    (await queryable.ToListAsync()).Count.ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/distinct_operator.cs#L54-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_strings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that the `Distinct()` keyword can be used with `Select()` transforms as well:

<!-- snippet: sample_get_distinct_numbers -->
<a id='snippet-sample_get_distinct_numbers'></a>
```cs
[Fact]
public async Task get_distinct_numbers()
{
    theSession.Store(new Target {Number = 1, Decimal = 1.0M});
    theSession.Store(new Target {Number = 1, Decimal = 2.0M});
    theSession.Store(new Target {Number = 1, Decimal = 2.0M});
    theSession.Store(new Target {Number = 2, Decimal = 1.0M});
    theSession.Store(new Target {Number = 2, Decimal = 2.0M});
    theSession.Store(new Target {Number = 2, Decimal = 1.0M});

    await theSession.SaveChangesAsync();

    var queryable = theSession.Query<Target>().Select(x => new
    {
        x.Number,
        x.Decimal
    }).Distinct();

    (await queryable.ToListAsync()).Count.ShouldBe(4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/distinct_operator.cs#L31-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_numbers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### DistinctBy()

Marten translates the LINQ `DistinctBy(keySelector)` operator to PostgreSQL's [`SELECT DISTINCT ON (key) ...`](https://www.postgresql.org/docs/current/sql-select.html#SQL-DISTINCT), keeping one row per distinct key value ([#4565](https://github.com/JasperFx/marten/issues/4565)). Use it after a `Select(...)` projection:

<!-- snippet: sample_distinct_by -->
<a id='snippet-sample_distinct_by'></a>
```cs
public async Task distinct_by(IDocumentSession session)
{
    // Keep one row per distinct Number. DistinctBy() is translated to
    // PostgreSQL `select distinct on (...)`, so it runs in the database
    // rather than pulling every row into memory.
    var results = await session.Query<Target>()
        .Select(x => new { x.Number, x.String })
        .DistinctBy(x => x.Number)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L20-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_distinct_by' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This generates roughly:

```sql
select distinct on (d.data ->> 'InstitutionId') jsonb_build_object(...) as data
from mt_doc_pendinginstitutionclaimline as d
order by d.data ->> 'InstitutionId'
```

Things to know:

- The key expression is automatically prepended to the `ORDER BY` because Postgres requires the `DISTINCT ON` expression to be the leftmost `ORDER BY` expression. Any `OrderBy()` you add becomes a secondary sort, so _which_ row survives within each key group follows your ordering (and is otherwise arbitrary, exactly like SQL `DISTINCT ON`).
- The `DistinctBy()` key must be a member that also exists on the queried document (the common case where the projection copies members through, e.g. `Select(x => new { x.InstitutionId, ... })`). `DistinctBy()` must be preceded by a `Select(...)` projection; calling it directly on the document (`Query<T>().DistinctBy(...)`) throws a `BadLinqExpressionException` — project first, or materialize and use `DistinctBy()` in memory.

## CountBy(), AggregateBy(), and Index() (.NET 9)

The `CountBy`, `AggregateBy`, and `Index` operators added in .NET 9 are **not translated to SQL by Marten**, because .NET only added them to `Enumerable` (there are no `IQueryable` overloads, so a query provider never sees them). Calling them on a Marten query therefore **runs client-side**: the full result set is pulled into memory first and the operator is applied there with LINQ to Objects — the same behavior you get with EF Core.

::: warning
`session.Query<Target>().CountBy(x => x.Color)` does **not** push the grouping into PostgreSQL — it materializes every matching document first. For large tables, express the grouping with `GroupBy(...).Select(...)`, which Marten does translate to a SQL `GROUP BY`:

```csharp
// Translated to SQL: select count(*), color ... group by color
var counts = await session.Query<Target>()
    .GroupBy(x => x.Color)
    .Select(g => new { Color = g.Key, Count = g.Count() })
    .ToListAsync();
```

(There is no efficient SQL equivalent for `AggregateBy`'s arbitrary accumulator function; `Index` corresponds to a window `row_number()` but is likewise only an in-memory operator today.) If/when .NET ships `Queryable` overloads for these, Marten can revisit native translation.
:::

## Modulo Queries

Marten has the ability to use the modulo operator in Linq queries:

<!-- snippet: sample_querying-with-modulo -->
<a id='snippet-sample_querying-with-modulo'></a>
```cs
[Fact]
public async Task use_modulo()
{
    theSession.Store(new Target{Color = Colors.Blue, Number = 1});
    theSession.Store(new Target{Color = Colors.Blue, Number = 2});
    theSession.Store(new Target{Color = Colors.Blue, Number = 3});
    theSession.Store(new Target{Color = Colors.Blue, Number = 4});
    theSession.Store(new Target{Color = Colors.Blue, Number = 5});
    theSession.Store(new Target{Color = Colors.Green, Number = 6});

    await theSession.SaveChangesAsync();

    (await theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color < Colors.Green).ToListAsync())
        .Select(x => x.Number)
        .ShouldHaveTheSameElementsAs(2, 4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/modulo_operator.cs#L11-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying-with-modulo' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
