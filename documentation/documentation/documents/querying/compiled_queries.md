<!--Title:Compiled Queries-->

Linq is easily one of the most popular features in .Net and arguably the one thing that other platforms strive to copy. We generally like being able
to express document queries in compiler-safe manner, but there is a non-trivial cost in parsing the resulting [Expression trees](https://msdn.microsoft.com/en-us/library/bb397951.aspx) and then using plenty of string concatenation to build up the matching SQL query. Fortunately, as of v0.8.10, Marten supports the concept of a _Compiled Query_ that you can use to reuse the SQL template for a given Linq query and bypass the performance cost of continuously parsing Linq expressions.

All compiled queries are classes that implement the `ICompiledQuery<TDoc, TResult>` interface shown below:

<[sample:ICompiledQuery]>

In its simplest usage, let's say that we want to find the first user document with a certain first name. That class would look like this:

<[sample:FindByFirstName]>

So a couple things to note in the class above:

1. The `QueryIs()` method returns an Expression representing a Linq query
1. `FindByFirstName` has a property (it could also be just a public field) called `FirstName` that is used to express the filter of the query

To use the `FindByFirstName` query, just use the code below:

<[sample:using-compiled-query]>

Or to use it as part of a batched query, this syntax:

<[sample:batch-query-with-compiled-queries]>


## How does it work?

The first time that Marten encounters a new type of `ICompiledQuery`, it executes the `QueryIs()` method and:

1. Parses the Expression just to find which property getters or fields are used within the expression as input parameters
1. Parses the Expression with our standard Linq support and to create a template database command and the internal query handler
1. Builds up an object with compiled Func's that "knows" how to read a query model object and set the command parameters for the query
1. Caches the resulting "plan" for how to execute a compiled query

On subsequent usages, Marten will just reuse the existing SQL command and remembered handlers to execute the query.


## What is supported?

To the best of our knowledge and testing, you may use any <[linkto:documentation/documents/querying/linq;title=Linq feature that Marten supports]> within a compiled query. So any combination of:

* `Select()` transforms
* `First/FirstOrDefault()`
* `Single/SingleOrDefault()`
* `Where()`
* `Include()`
* `OrderBy/OrderByDescending` etc.
* `Count()`
* `Any()`
* `AsJson()`
* `ToJsonArray()`
* `ToJsonArrayAsync()`
* `Skip()`, `Take()` and `Stats()` for pagination

At this point (v0.9), the only limitation is that you cannot use the Linq `ToArray()` or `ToList()` operators. See the next section for an explanation of how to query for multiple results.



## Querying for multiple results

To query for multiple results, you need to just return the raw `IQueryable<T>` as `IEnumerable<T>` as the result type. You cannot use the `ToArray()` or `ToList()` operators (it'll throw exceptions from the Relinq library if you try). As a convenience mechanism, Marten supplies these helper interfaces:

If you are selecting the whole document without any kind of `Select()` transform, you can use this interface:

<[sample:ICompiledListQuery-with-no-select]>

A sample usage of this type of query is shown below:

<[sample:UsersByFirstName-Query]>

If you do want to use a `Select()` transform, use this interface:

<[sample:ICompiledListQuery-with-select]>

A sample usage of this type of query is shown below:

<[sample:UserNamesForFirstName]>



## Querying for included documents

If you wish to use a compiled query for a document, using a `JOIN` so that the query will include another document, just as the <[linkto:documentation/documents/querying/include;title=Include()]> method does on a simple query, the compiled query would be constructed just like any other, using the `Include()` method
on the query:

<[sample:compiled_include]>

In this example, the query has an `Included` property which will receive the included Assignee / `User`. The 'resulting' included property can only be
a property of the query, so that Marten would know how to assign the included result of the postgres query.
The `JoinType` property here is just an example for overriding the default `INNER JOIN`. If you wish to force an `INNER JOIN` within the query
you can simply remove the `JoinType` parameter like so: `.Include<Issue, IssueByTitleWithAssignee>(x => x.AssigneeId, x => x.Included)`

You can also chain `Include` methods if you need more than one `JOIN`s.

### Querying for multiple included documents

Fetching "included" documents could also be done when you wish to include multiple documents.
So picking up the same example, if you wish to get a list of `Issue`s and for every Issue you wish to retrieve
its' Assignee / `User`, in your compiled query you should have a list of `User`s like so:

<[sample:compiled_include_list]>

Note that you could either have the list instantiated or at least make sure the property has a setter as well as a getter (we've got your back).

As with the simple include queries, you could also use a Dictionary with a key type corresponding to the Id of the document- the dictionary value type:

<[sample:compiled_include_dictionary]>



## Querying for paginated results

Marten compiled queries also support queries for paginated results, where you could specify the page number and size, as well as getting the total count.
A simple example of how this can be achieved as follows:

<[sample:compiled-query-statistics]>

Note that the way to get the `QueryStatistics` out is done by having a property on the query, which we specify in the `Stats()` method, similarly to the way 
we handle Include queries.

## Querying for a single document

If you are querying for a single document with no transformation, you can use this interface as a convenience:

<[sample:ICompiledQuery-for-single-doc]>

And an example:

<[sample:FindUserByAllTheThings]>



## Querying for multiple results as Json

To query for multiple results and have them returned as a Json string, you may run any query on your `IQueryable<T>` (be it ordering or filtering) and then simply finalize the query with `ToJsonArray();` like so:

<[sample:CompiledToJsonArray]>

If you wish to do it asynchronously, you can use the `ToJsonArrayAsync()` method.

A sample usage of this type of query is shown below:

<[sample:FindJsonOrderedUsersByUsername]>

Note that the result has the documents comma separated and wrapped in angle brackets (as per the Json notation).



## Querying for a single document

Finally, if you are querying for a single document as json, you will need to prepend your call to `Single()`, `First()` and so on with a call to `AsJson()`:

<[sample:CompiledAsJson]>

And an example:

<[sample:FindJsonUserByUsername]>

(our `ToJson()` method simply returns a string representation of the `User` instance in Json notation)
