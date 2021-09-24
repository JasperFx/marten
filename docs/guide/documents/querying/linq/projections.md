# Projection Operators


### SelectMany()

Marten 1.2 adds the ability to use the `SelectMany()` operator to issue queries against child collections. You can use
`SelectMany()` against primitive collections like so:

<!-- snippet: sample_can_do_simple_select_many_against_simple_array -->
<a id='snippet-sample_can_do_simple_select_many_against_simple_array'></a>
```cs
[Fact]
public void can_do_simple_select_many_against_simple_array()
{
    var product1 = new Product {Tags = new[] {"a", "b", "c"}};
    var product2 = new Product {Tags = new[] {"b", "c", "d"}};
    var product3 = new Product {Tags = new[] {"d", "e", "f"}};

    using (var session = theStore.OpenSession())
    {
        session.Store(product1, product2, product3);
        session.SaveChanges();
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_select_many.cs#L20-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_can_do_simple_select_many_against_simple_array' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_select_many.cs#L394-L402' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-select-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A few notes on the `SelectMany()` usage and limitations:

* As of 1.2, you are only able to use a single `SelectMany()` operator in a single Linq query. That limitation will be removed in 1.3.
* You can use any other Linq operator that Marten supports *after* the `SelectMany()` in a Linq query, including the `Stats()` and `Include()` operators
* `Take()` and `Skip()` operators in a Linq query that contains a `SelectMany()` operator will always apply to the child collection database
  rather than the parent document regardless of the order in which the operators appear in the Linq query
* You cannot use `SelectMany()` with both a `Distinct()` and a `Count()` operator at this point.




