# Document Projections

Marten has now the capacity to run [projection queries](https://en.wikipedia.org/wiki/Projection_(relational_algebra)), where only specific document properties are retrieved. The projection queries are executed by using the linq `IQueryable.Select()` method.

## Projection Queries

When you wish to retrieve an IEnumerable of a certain document property for example:

<<< @/../src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#sample_one_field_projection

When you wish to retrieve certain properties and transform them into another type:

<<< @/../src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#sample_other_type_projection

When you wish to retrieve certain properties and transform them into an anonymous type:

<<< @/../src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#sample_anonymous_type_projection

Marten also allows you to run projection queries on deep (nested) properties:

<<< @/../src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#sample_deep_properties_projection

## Chaining other Linq Methods

After calling Select, you'd be able to chain other linq methods such as `First()`, `FirstOrDefault()`, `Single()` and so on, like so:

<<< @/../src/Marten.Testing/Linq/invoking_query_with_select_Tests.cs#sample_get_first_projection

## Async Projections

Marten also supports asynchronously running projection queries. You'd be able to achieve this by simply chaining the asynchronous resolving method you are after. For example:

* `ToListAsync()`
* `FirstAsync()`
* `SingleOrDefaultAsync()`

And so on...
