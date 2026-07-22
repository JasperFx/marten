# Projection Operators

## Select()

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
[SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L209-L231' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_other_type_projection' title='Start of snippet'>anchor</a></sup>
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

    await theSession.SaveChangesAsync();(await 

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
        .ToListAsync())
        .Select(x => x.Name)
        .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L169-L186' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_anonymous_type_projection' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L321-L336' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_properties_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Server-Side JSON Projections <Badge type="tip" text="9.18" />

::: tip
This optimization applies automatically. There is nothing you need to opt into -- Marten
just tries to push as much of the projection down into SQL as it safely can.
:::

When a `Select()` projection is a "simple" transform -- meaning its constructor arguments
and/or property initializers are made up **only** of direct (optionally nested) member
accesses on the source document, with no method calls, arithmetic, string concatenation,
casts, or conditional (`? :`) expressions -- Marten translates the whole projection into a
single `jsonb_build_object(...)` expression that Postgres itself evaluates, rather than
pulling the full document down to the client and reshaping it in .NET. For example:

```cs
var dtos = await session.Query<User>()
    .Select(x => new UserName { Name = x.FirstName })
    .ToListAsync();
```

is executed as something close to:

```sql
select jsonb_build_object('Name', d.data ->> 'FirstName') as data
from mt_doc_user as d
```

The generated JSON key names follow the same naming policy (e.g. camelCase) as the rest of
your serialized documents, and this also works through nested member access:

```cs
// x.Client.Name becomes d.data -> 'client' -> 'name'
.Select(x => new { ClientName = x.Client.Name })
```

`Distinct()` composes naturally with this optimization, and is applied as `distinct on`/
`distinct` over the generated `jsonb_build_object(...)` expression.

Because the projected shape is built by Postgres as a `jsonb` value, it can be streamed
directly to the client as raw JSON with no further (de)serialization on the Marten side --
see [Query for Raw JSON](/documents/querying/query-json) for `ToJsonArray()`/`ToJsonFirst()`
and the raw `StreamJsonArray()`/`StreamOne()`/`StreamMany()` methods on `IQueryable<T>`.

### Projections That Can't Be Expressed in SQL

If a `Select()` projection contains anything Marten can't safely translate into
`jsonb_build_object(...)` -- a method call (`x.Name.ToUpper()`), arithmetic, string
interpolation, a cast, or a conditional expression -- Marten instead deserializes the full
source document and applies your original projection lambda on the client, exactly as it
always has. This fallback always produces correct results, but because the underlying
`data` column no longer matches the shape of `TResult`, it **cannot** be streamed as raw
JSON: calling `StreamJsonArray()`/`StreamOne()`/`ToJsonArray()` (or similar) against such a
projection will throw a `BadLinqExpressionException` telling you to use `ToListAsync()` (or
another non-streaming method) instead.

## Chaining other Linq Methods

After calling Select, you'd be able to chain other linq methods such as `First()`, `FirstOrDefault()`, `Single()` and so on, like so:

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

## SelectMany()

::: top
As of Marten V4, you can chain `SelectMany()` operators N-deep with any possible `Where()` / `OrderBy` / `Distinct()` / etc
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

A few notes on the `SelectMany()` usage and limitations:

* You can use any other Linq operator that Marten supports *after* the `SelectMany()` in a Linq query, including the `Stats()` and `Include()` operators
* `Take()` and `Skip()` operators in a Linq query that contains a `SelectMany()` operator will always apply to the child collection database
  rather than the parent document regardless of the order in which the operators appear in the Linq query
* You cannot use `SelectMany()` with both a `Distinct()` and a `Count()` operator at this point.
