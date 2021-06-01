# Document Projections

Marten has now the capacity to run [projection queries](https://en.wikipedia.org/wiki/Projection_(relational_algebra)), where only specific document properties are retrieved. The projection queries are executed by using the linq `IQueryable.Select()` method.

## Projection Queries

When you wish to retrieve an IEnumerable of a certain document property for example:

<!-- snippet: sample_one_field_projection -->
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into another type:

<!-- snippet: sample_other_type_projection -->
<!-- endSnippet -->

When you wish to retrieve certain properties and transform them into an anonymous type:

<!-- snippet: sample_anonymous_type_projection -->
<!-- endSnippet -->

Marten also allows you to run projection queries on deep (nested) properties:

<!-- snippet: sample_deep_properties_projection -->
<!-- endSnippet -->

## Chaining other Linq Methods

After calling Select, you'd be able to chain other linq methods such as `First()`, `FirstOrDefault()`, `Single()` and so on, like so:

<!-- snippet: sample_get_first_projection -->
<!-- endSnippet -->

## Async Projections

Marten also supports asynchronously running projection queries. You'd be able to achieve this by simply chaining the asynchronous resolving method you are after. For example:

* `ToListAsync()`
* `FirstAsync()`
* `SingleOrDefaultAsync()`

And so on...
