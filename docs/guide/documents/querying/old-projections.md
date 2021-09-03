# Document Projections

Marten has now the capacity to run [projection queries](https://en.wikipedia.org/wiki/Projection_(relational_algebra)), where only specific document properties are retrieved. The projection queries are executed by using the linq `IQueryable.Select()` method.

## Projection Queries

When you wish to retrieve an IEnumerable of a certain document property for example:

<!-- snippet: sample_one_field_projection -->
<a id='snippet-sample_one_field_projection'></a>
```cs
[Fact]
public void use_select_in_query_for_one_field()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    theSession.SaveChanges();

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
        .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#L16-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_one_field_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into another type:

<!-- snippet: sample_other_type_projection -->
<a id='snippet-sample_other_type_projection'></a>
```cs
[SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
public void use_select_with_multiple_fields_to_other_type()
{
    theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
    theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
    theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
    theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

    theSession.SaveChanges();

    var users = theSession.Query<User>().Select(x => new User2 { First = x.FirstName, Last = x.LastName }).ToList();

    users.Count.ShouldBe(4);

    users.Each(x =>
    {
        SpecificationExtensions.ShouldNotBeNull(x.First);
        SpecificationExtensions.ShouldNotBeNull(x.Last);
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#L190-L212' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_other_type_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into an anonymous type:

<!-- snippet: sample_anonymous_type_projection -->
<a id='snippet-sample_anonymous_type_projection'></a>
```cs
[Fact]
public void use_select_to_transform_to_an_anonymous_type()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    theSession.SaveChanges();

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
        .ToArray()
        .Select(x => x.Name)
        .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#L150-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_anonymous_type_projection' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#L302-L317' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_properties_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Chaining other Linq Methods

After calling Select, you'd be able to chain other linq methods such as `First()`, `FirstOrDefault()`, `Single()` and so on, like so:

<!-- snippet: sample_get_first_projection -->
<a id='snippet-sample_get_first_projection'></a>
```cs
[Fact]
public void use_select_to_another_type_with_first()
{
    theSession.Store(new User { FirstName = "Hank" });
    theSession.Store(new User { FirstName = "Bill" });
    theSession.Store(new User { FirstName = "Sam" });
    theSession.Store(new User { FirstName = "Tom" });

    theSession.SaveChanges();

    theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
        .FirstOrDefault()
        ?.Name.ShouldBe("Bill");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#L78-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_first_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Async Projections

Marten also supports asynchronously running projection queries. You'd be able to achieve this by simply chaining the asynchronous resolving method you are after. For example:

* `ToListAsync()`
* `FirstAsync()`
* `SingleOrDefaultAsync()`

And so on...
