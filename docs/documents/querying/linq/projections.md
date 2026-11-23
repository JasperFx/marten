# Projection Operators

## Select() {#select}

When you wish to retrieve an IEnumerable of a certain document property for example:

<!-- snippet: sample_one_field_projection -->
<a id='snippet-sample_one_field_projection'></a>
```cs
[Fact]
public async Task use_select_in_query_for_one_field()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    await theSession.SaveChangesAsync();

    (await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).ToListAsync())
        .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L16-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_one_field_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into another type:

<!-- snippet: sample_other_type_projection -->
<a id='snippet-sample_other_type_projection'></a>
```cs
[Fact]
public async Task use_select_with_multiple_fields_to_other_type()
{
    theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
    theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
    theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
    theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

    await theSession.SaveChangesAsync();

    var users = (await theSession.Query<User>().Select(x => new User2 { First = x.FirstName, Last = x.LastName }).ToListAsync());

    users.Count.ShouldBe(4);

    users.Each(x =>
    {
        x.First.ShouldNotBeNull();
        x.Last.ShouldNotBeNull();
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L213-L235' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_other_type_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into an anonymous type:

<!-- snippet: sample_anonymous_type_projection -->
<a id='snippet-sample_anonymous_type_projection'></a>
```cs
[Fact]
public async Task use_select_to_transform_to_an_anonymous_type()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    await theSession.SaveChangesAsync();

    var names = await theSession.Query<User>()
        .OrderBy(x => x.FirstName)
        .Select(x => new { Name = x.FirstName })
        .ToListAsync();

    names.Select(x => x.Name).ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L169-L188' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_anonymous_type_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten also allows you to run projection queries on deep (nested) properties:

<!-- snippet: sample_deep_properties_projection -->
<a id='snippet-sample_deep_properties_projection'></a>
```cs
[Fact]
public async Task transform_with_deep_properties()
{
    var targets = Target.GenerateRandomData(100).ToArray();

    await theStore.BulkInsertAsync(targets);

    var actual = (await theSession.Query<Target>().Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).ToListAsync()).Distinct();

    var expected = targets.Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).Distinct();

    actual.ShouldHaveTheSameElementsAs(expected);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L327-L342' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_properties_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Server-Side JSON Projections <Badge type="tip" text="9.18" /> {#server-side-json-projections}

::: tip
This optimization applies automatically. There is nothing you need to opt into -- Marten
just tries to push as much of the projection down into SQL as it safely can.
:::

When a `Select()` projection into an object is a "simple" transform -- meaning its constructor arguments
and/or property initializers are made up **only** of direct (optionally nested) member
accesses on the source document, with no method calls, arithmetic, string concatenation,
lossy casts, or conditional (`? :`) expressions -- Marten translates the whole projection into a
single `jsonb_build_object(...)` expression that Postgres itself evaluates, rather than
pulling the full document down to the client and reshaping it in .NET. For example:

<!-- snippet: sample_server_side_json_projection -->
<a id='snippet-sample_server_side_json_projection'></a>
```cs
public async Task server_side_json_projection(IQuerySession session)
{
    // Only member access, so PostgreSQL builds the JSON with jsonb_build_object()
    var names = await session.Query<User>()
        .Select(x => new UserName { Name = x.FirstName })
        .ToListAsync();

    // A nested member is read through its JSON path, d.data -> 'Inner' ->> 'Number'
    var inner = await session.Query<Target>()
        .Select(x => new { x.Id, InnerNumber = x.Inner.Number })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L35-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_server_side_json_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The first query is executed as something close to:

```sql
select jsonb_build_object('Name', d.data ->> 'FirstName') as data
from mt_doc_user as d
```

The generated JSON key names follow the same naming policy (e.g. camelCase) as the rest of
your serialized documents, and nested members are read through their JSON path.

`Distinct()` composes naturally with this optimization, and is applied as `distinct on`/
`distinct` over the generated `jsonb_build_object(...)` expression.

Because the projected shape is built by Postgres as a `jsonb` value, it can be streamed
directly to the client as raw JSON with no further (de)serialization on the Marten side.
See [Query for Raw JSON](/documents/querying/query-json) for `ToJsonArray()`, `ToJsonFirst()`,
`StreamJsonArray()`, `StreamJsonFirst()` and the other raw JSON methods on `IQueryable<T>`.

A document written before a property was added to its class has no key for that property. The projection leaves the
key out too, rather than writing an explicit `null`, so a non-nullable member of the target type gets its default
value when the result is read. The same holds for the JSON that `ToJsonArray()` and `StreamJsonArray()` return.

### Counting a Child Collection {#counting-a-child-collection}

::: tip
Available in Marten 9.24 and later.
:::

`Count()` over a child collection is the one method call a projection into an object can keep in SQL, because
Postgres already has an expression for it. Both the whole collection and a filtered subset are
computed server-side:

<!-- snippet: sample_count_a_child_collection_in_select -->
<a id='snippet-sample_count_a_child_collection_in_select'></a>
```cs
public async Task count_a_child_collection_in_select(IQuerySession session)
{
    var counts = await session.Query<Order>()
        .Select(x => new
        {
            x.Id,
            LineCount = x.Lines.Count(),
            ActiveLineCount = x.Lines.Count(line => line.IsActive),
            FooCount = x.Lines.Count(line => line.Product == "foo")
        })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L51-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_count_a_child_collection_in_select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

becomes something close to:

```sql
select jsonb_build_object(
    'Id', d.id,
    'LineCount', jsonb_array_length(d.data -> 'Lines'),
    'ActiveLineCount', jsonb_array_length(jsonb_path_query_array(d.data, '$.Lines[*] ? (@.IsActive == $val1)', :p0)),
    'FooCount', jsonb_array_length(jsonb_path_query_array(d.data, '$.Lines[*] ? (@.Product == $val1)', :p1))
) as data
from mt_doc_order as d
```

This is the same translation `Where(x => x.Lines.Count(l => ...) > n)` uses, so a count in a
projection and the same count in a filter always agree. A predicate is translated when every part
of it is expressible as a JSONPath filter -- equality and the other comparison operators, `&&`,
and the string methods (`StartsWith`, `EndsWith`, `Contains`) -- and a bare boolean member such as
`line.IsActive` counts as equality against `true`.

Anything outside that set, notably `||`, falls back to the client-side projection described below
rather than being narrowed to something that would return a different count.

### Projections That Can't Be Expressed in SQL {#client-side-projections}

If a `Select()` projection **into an object** -- an anonymous type, a DTO or a record -- contains anything Marten can't
safely translate into `jsonb_build_object(...)` -- a method call (`x.Name.ToUpper()`), arithmetic, string
interpolation, a *lossy* cast, or a conditional expression -- Marten instead deserializes the
full source document and applies your original projection lambda on the client, exactly as it
always has. This fallback always produces correct results, but because the underlying
`data` column no longer matches the shape of `TResult`, it **cannot** be streamed as raw
JSON: calling `ToJsonArray()`, `StreamJsonArray()` or another raw JSON method against such a
projection will throw a `BadLinqExpressionException` telling you to use `ToListAsync()` (or
another non-streaming method) instead.

A computed `Select()` of a single value, such as `Select(x => x.Name.ToUpper())`, does not fall back to the client.
Marten computes it in SQL or refuses it, as described in [Computed Values in Select()](#computed-values-in-select).

Value-preserving conversions do **not** trigger the fallback: a widening numeric cast
(`int` &rarr; `long`/`decimal`), boxing to `object`, wrapping in a nullable (`int` &rarr;
`int?`), or an `enum` &rarr; its underlying integral (when enums are stored as integers) all
keep translating to `jsonb_build_object(...)` and can still be streamed as raw JSON, because
the stored JSON value already equals the converted value. Only genuinely lossy or computed
conversions
(narrowing casts, `enum`&harr;`string`, `Convert.ToXxx`) fall back.

## Computed Values in Select() {#computed-values-in-select}

A `Select()` of a single value can compute it from the document's members. Marten computes it in SQL, and any
operator after the `Select()` -- `Where()`, `OrderBy()`, `Distinct()`, `Take()`, `Skip()`, `CountAsync()`,
`FirstAsync()`, `SumAsync()` and the other aggregates -- works on the computed value:

<!-- snippet: sample_computed_values_in_select -->
<a id='snippet-sample_computed_values_in_select'></a>
```cs
public async Task computed_values_in_select(IQuerySession session)
{
    // Each of these is computed by PostgreSQL, not on the client
    var remainders = await session.Query<Target>().Select(x => x.Number % 3).ToListAsync();
    var labels = await session.Query<Target>().Select(x => x.String + "-" + x.Number).ToListAsync();
    var upper = await session.Query<Target>().Select(x => x.String.ToUpper()).ToListAsync();
    var ids = await session.Query<Target>().Select(x => x.Id.ToString()).ToListAsync();
    var widened = await session.Query<Target>().Select(x => (long)x.Number * 1_000_000_000L).ToListAsync();

    // Operators after the Select() work on the computed value
    var ones = await session.Query<Target>()
        .OrderBy(x => x.Number)
        .Select(x => x.Number % 3)
        .Where(r => r == 1)
        .ToListAsync();

    var distinct = await session.Query<Target>().Select(x => x.Number % 3).Distinct().ToListAsync();
    var ordered = await session.Query<Target>().Select(x => x.Number % 3).OrderBy(r => r).ToListAsync();
    var total = await session.Query<Target>().SumAsync(x => x.Number % 3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L67-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_computed_values_in_select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten translates member access, arithmetic and `%` on integers, arithmetic on floating point numbers, widening
casts, an enum cast to its underlying integer when enums are stored as integers, string concatenation, `ToUpper()`,
`ToLower()`, `Length`, and `ToString()` of an integer or a `Guid`.

A value PostgreSQL would not compute the way .NET does throws `BadLinqExpressionException`: a `Math` call, a
conditional (`? :`), `Substring()`, decimal division, a narrowing cast, `%` on floating point numbers, or `Count()` of
a child collection inside arithmetic. Marten does not fall back to the client here. To compute one of those, select
the members you need and compute the value in memory, or project into an object, which
[runs on the client](#client-side-projections) when it can't be expressed in SQL.

## Operators after Select() {#operators-after-select}

`Where()` after selecting a child object, `Select(x => x.Inner).Where(i => ...)`, filters the documents on that child.
`OrderBy()` after a `Select()` of a single value orders the projected values:

<!-- snippet: sample_operators_after_select -->
<a id='snippet-sample_operators_after_select'></a>
```cs
public async Task operators_after_select(IQuerySession session)
{
    // Where() after selecting a child object filters the documents on that child
    var inners = await session.Query<Target>()
        .Select(x => x.Inner)
        .Where(i => i != null && i.Number > 0)
        .ToListAsync();

    // OrderBy() after a scalar Select() orders the projected values
    var numbers = await session.Query<Target>()
        .Select(x => x.Number)
        .OrderByDescending(n => n)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L91-L106' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_operators_after_select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`OrderBy()` after a `Select()` into an anonymous type, a DTO or another document throws
`BadLinqExpressionException`. Order the documents before the `Select()` instead.

## Raw SQL in Select() {#explicit-sql-in-select}

`ExplicitSql<T>(sql)` puts a SQL expression of your own into a projection. Use `d` as the alias of the document
table. It works as the whole projection, `Select(x => x.ExplicitSql<string>("d.data ->> 'FirstName'"))`, and as a
member of an object projection:

<!-- snippet: sample_explicit_sql_in_select -->
<a id='snippet-sample_explicit_sql_in_select'></a>
```cs
var firstNames = await theSession.Query<User>()
    .Where(x => x.FirstName == "Hank")
    .Select(x => new { Name = x.ExplicitSql<string>("d.data ->> 'FirstName'") })
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/using_explicit_sql_in_select_clauses.cs#L45-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_explicit_sql_in_select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Chaining other Linq Methods {#chaining-other-linq-methods}

After calling Select, you'd be able to chain other linq methods such as `FirstAsync()`, `FirstOrDefaultAsync()`, `SingleAsync()` and so on, like so:

<!-- snippet: sample_get_first_projection -->
<a id='snippet-sample_get_first_projection'></a>
```cs
[Fact]
public async Task use_select_to_another_type_with_first()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    await theSession.SaveChangesAsync();

    (await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
        .FirstOrDefaultAsync())
        ?.Name.ShouldBe("Bill");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L97-L113' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_first_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## SelectMany() {#selectmany}

::: tip
You can chain `SelectMany()` operators N-deep with any possible `Where()` / `OrderBy` / `Distinct()` / etc
operators
:::

Marten has the ability to use the `SelectMany()` operator to issue queries against child collections. You can use
`SelectMany()` against primitive collections like so:

<!-- snippet: sample_can_do_simple_select_many_against_simple_array -->
<a id='snippet-sample_can_do_simple_select_many_against_simple_array'></a>
```cs
[Fact]
public async Task can_do_simple_select_many_against_simple_array()
{
    var product1 = new Product {Tags = ["a", "b", "c"]};
    var product2 = new Product {Tags = ["b", "c", "d"]};
    var product3 = new Product {Tags = ["d", "e", "f"]};

    using (var session = theStore.LightweightSession())
    {
        session.Store(product1, product2, product3);
        await session.SaveChangesAsync();
    }

    using (var query = theStore.QuerySession())
    {
        var distinct = (await query.Query<Product>().SelectMany(x => x.Tags).Distinct().ToListAsync());

        distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs("a", "b", "c", "d", "e", "f");

        var names = (await query.Query<Product>().SelectMany(x => x.Tags).ToListAsync());
        names
            .Count.ShouldBe(9);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_many.cs#L16-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_can_do_simple_select_many_against_simple_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or against collections of child documents:

<!-- snippet: sample_using-select-many -->
<a id='snippet-sample_using-select-many'></a>
```cs
var results = (await query.Query<Target>()
    .SelectMany(x => x.Children)
    .Where(x => x.Flag)
    .OrderBy(x => x.Id)
    .Skip(20)
    .Take(15)
    .ToListAsync());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_many.cs#L385-L393' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-select-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Include()`, `Stats()`, `Distinct()` and `CountAsync()` all work after a `SelectMany()`, including `Distinct()`
followed by `CountAsync()`.

### Operators after SelectMany() {#operators-after-selectmany}

A `Select()` after `SelectMany()` is computed in SQL over the flattened rows, with the same translations as
[Computed Values in Select()](#computed-values-in-select). `Where()`, `OrderBy()`, `Distinct()`, `Take()`, `Skip()`,
`FirstAsync()`, `SingleAsync()`, `CountAsync()`, `AnyAsync()` and the aggregates then apply to the projected value.
Before the `Select()`, `Where()` and `OrderBy()` can use `Length`, `ToUpper()` and `ToLower()` of a flattened string:

<!-- snippet: sample_operators_after_select_many -->
<a id='snippet-sample_operators_after_select_many'></a>
```cs
public async Task operators_after_select_many(IQuerySession session)
{
    // Filter the flattened values, compute a value from each, then filter that
    var ones = await session.Query<Target>()
        .OrderBy(x => x.Number)
        .SelectMany(x => x.NumberArray)
        .Where(n => n > 25)
        .Select(n => n % 3)
        .Where(r => r == 1)
        .ToListAsync();

    // Order, de-duplicate and page the computed values
    var lengths = await session.Query<Target>()
        .SelectMany(x => x.StringArray)
        .Select(s => s.Length)
        .Distinct()
        .ToListAsync();

    var upper = await session.Query<Target>()
        .SelectMany(x => x.StringArray)
        .Select(s => s.ToUpper())
        .OrderBy(s => s)
        .ToListAsync();

    // A member of each child document
    var childNumbers = await session.Query<Target>()
        .OrderBy(x => x.Number)
        .SelectMany(x => x.Children)
        .Where(c => c.Flag)
        .Select(c => c.Number)
        .Take(10)
        .ToListAsync();

    // Element operators and aggregates over the flattened rows
    var firstChild = await session.Query<Target>().SelectMany(x => x.Children).FirstOrDefaultAsync();
    var longest = await session.Query<Target>().SelectMany(x => x.StringArray).Select(s => s.Length)
        .MaxAsync(l => l);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L110-L149' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_operators_after_select_many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A `Select()` of the flattened rows into an anonymous type or a DTO works on its own, or followed by `Take()` and
`Skip()`. Followed by `Where()`, `OrderBy()`, `Distinct()` or an aggregate, it throws `BadLinqExpressionException`, and
so does a `GroupBy()` after a projection of the flattened rows. Apply those operators before the `Select()`.

### Paging and SelectMany() {#paging-and-selectmany}

`Take()` and `Skip()` before a `SelectMany()` page the documents, so the result is every child of the documents on
that page. After the `SelectMany()`, they page the flattened rows:

<!-- snippet: sample_paging_before_and_after_select_many -->
<a id='snippet-sample_paging_before_and_after_select_many'></a>
```cs
public async Task paging_before_and_after_select_many(IQuerySession session)
{
    // Every child of the second page of five documents
    var childrenOfAPage = await session.Query<Target>()
        .OrderBy(x => x.Number)
        .Skip(5)
        .Take(5)
        .SelectMany(x => x.Children)
        .ToListAsync();

    // The second page of five children, across all the documents
    var pageOfChildren = await session.Query<Target>()
        .OrderBy(x => x.Number)
        .SelectMany(x => x.Children)
        .Skip(5)
        .Take(5)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/projections_documentation_samples.cs#L152-L171' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_paging_before_and_after_select_many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`CountAsync()` over either shape counts the rows the query would return.
