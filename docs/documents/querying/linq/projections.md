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

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
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

    var users = theSession.Query<User>().Select(x => new User2 { First = x.FirstName, Last = x.LastName }).ToList();

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

    await theSession.SaveChangesAsync();

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
        .ToArray()
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
public void transform_with_deep_properties()
{
    var targets = Target.GenerateRandomData(100).ToArray();

    theStore.BulkInsert(targets);

    var actual = theSession.Query<Target>().Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).ToList().Distinct();

    var expected = targets.Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).Distinct();

    actual.ShouldHaveTheSameElementsAs(expected);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_clause_usage.cs#L321-L336' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_properties_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
        .FirstOrDefault()
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
    var product1 = new Product {Tags = new[] {"a", "b", "c"}};
    var product2 = new Product {Tags = new[] {"b", "c", "d"}};
    var product3 = new Product {Tags = new[] {"d", "e", "f"}};

    using (var session = theStore.LightweightSession())
    {
        session.Store(product1, product2, product3);
        await session.SaveChangesAsync();
    }

    using (var query = theStore.QuerySession())
    {
        var distinct = query.Query<Product>().SelectMany(x => x.Tags).Distinct().ToList();

        distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs("a", "b", "c", "d", "e", "f");

        var names = query.Query<Product>().SelectMany(x => x.Tags).ToList();
        names
            .Count().ShouldBe(9);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_many.cs#L20-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_can_do_simple_select_many_against_simple_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or against collections of child documents:

<!-- snippet: sample_using-select-many -->
<a id='snippet-sample_using-select-many'></a>
```cs
var results = query.Query<Target>()
    .SelectMany(x => x.Children)
    .Where(x => x.Flag)
    .OrderBy(x => x.Id)
    .Skip(20)
    .Take(15)
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/select_many.cs#L389-L397' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-select-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A few notes on the `SelectMany()` usage and limitations:

* As of 1.2, you are only able to use a single `SelectMany()` operator in a single Linq query. That limitation will be removed in 1.3.
* You can use any other Linq operator that Marten supports *after* the `SelectMany()` in a Linq query, including the `Stats()` and `Include()` operators
* `Take()` and `Skip()` operators in a Linq query that contains a `SelectMany()` operator will always apply to the child collection database
  rather than the parent document regardless of the order in which the operators appear in the Linq query
* You cannot use `SelectMany()` with both a `Distinct()` and a `Count()` operator at this point.
