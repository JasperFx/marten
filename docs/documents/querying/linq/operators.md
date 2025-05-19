# Supported Linq Operators

## Element Operations

Marten has been successfully tested with these [element operations](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/element-operations):

1. `First()`
1. `FirstAsync()` -- Marten specific
1. `Single()`
1. `SingleAsync()` -- Marten specific
1. `FirstOrDefault()`
1. `FirstOrDefaultAsync()` -- Marten specific
1. `SingleOrDefault()`
1. `SingleOrDefaultAsync()` -- Marten specific

<!-- snippet: sample_select_a_single_value -->
<a id='snippet-sample_select_a_single_value'></a>
```cs
public void select_a_single_value(IDocumentSession session)
{
    // Single()/SingleOrDefault() will throw exceptions if more than
    // one result is returned from the database
    session.Query<Target>().Where(x => x.Number == 5).Single();
    session.Query<Target>().Where(x => x.Number == 5).SingleOrDefault();

    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).First();
    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).FirstOrDefault();

    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).Last();
    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).LastOrDefault();

    // Using the query inside of Single/Last/First is supported as well
    session.Query<Target>().Single(x => x.Number == 5);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L138-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_select_a_single_value' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L20-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_basic_operators' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L50-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_nested_properties' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L38-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_and_or_or' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L85-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ordering-in-linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L103-L127' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ordering-in-linq-using-dynamic-props' title='Start of snippet'>anchor</a></sup>
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

Marten has been successfully tested with these [aggregation operators](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/aggregation-operations):

1. `Count()` / `CountAsync()`
1. `LongCount()` / `LongCountAsync()`
1. `Min()` / `MinAsync()`
1. `Max()` / `MaxAsync()`
1. `Sum()` / `SumAsync()`
1. `Average()` / `AverageAsync()`

<!-- snippet: sample_aggregation_operations -->
<a id='snippet-sample_aggregation_operations'></a>
```cs
public async Task sample_aggregation_operations(IQuerySession session)
{
    var count = session.Query<Target>().Count();
    var count2 = await session.Query<Target>().CountAsync();
    var count3 = session.Query<Target>().LongCount();
    var count4 = await session.Query<Target>().LongCountAsync();

    var min = await session.Query<Target>().MinAsync(x => x.Number);
    var max = await session.Query<Target>().MaxAsync(x => x.Number);
    var sum = await session.Query<Target>().SumAsync(x => x.Number);
    var average = await session.Query<Target>().AverageAsync(x => x.Number);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L190-L205' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregation_operations' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Partitioning Operators

Marten has been successfully tested with these [partition operators](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/partitioning-data):

1. `Take()`
1. `Skip()`

<!-- snippet: sample_using_take_and_skip -->
<a id='snippet-sample_using_take_and_skip'></a>
```cs
public void using_take_and_skip(IDocumentSession session)
{
    // gets records 11-20 from the database
    session.Query<Target>().Skip(10).Take(10).OrderBy(x => x.Number).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L129-L136' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_take_and_skip' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

TODO -- link to the paging support

## Grouping Operators

Sorry, but Marten does not yet support `GroupBy()`. You can track [this GitHub issue](https://github.com/JasperFx/marten/issues/569) to follow
any future work on this Linq operator.

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

    queryable.ToList().Count.ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/distinct_operator.cs#L56-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_strings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that the `Distinct()` keyword can be used with `Select()` transforms as well:

<!-- snippet: sample_get_distinct_numbers -->
<a id='snippet-sample_get_distinct_numbers'></a>
```cs
[SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
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

    queryable.ToList().Count.ShouldBe(4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/distinct_operator.cs#L33-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_numbers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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

    theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color < Colors.Green).ToArray()
        .Select(x => x.Number)
        .ShouldHaveTheSameElementsAs(2, 4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/modulo_operator.cs#L10-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying-with-modulo' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
