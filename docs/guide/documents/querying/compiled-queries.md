# Compiled Queries

Linq is easily one of the most popular features in .Net and arguably the one thing that other platforms strive to copy. We generally like being able
to express document queries in compiler-safe manner, but there is a non-trivial cost in parsing the resulting [Expression trees](https://msdn.microsoft.com/en-us/library/bb397951.aspx) and then using plenty of string concatenation to build up the matching SQL query. Fortunately, as of v0.8.10, Marten supports the concept of a _Compiled Query_ that you can use to reuse the SQL template for a given Linq query and bypass the performance cost of continuously parsing Linq expressions.

All compiled queries are classes that implement the `ICompiledQuery<TDoc, TResult>` interface shown below:

<!-- snippet: sample_ICompiledQuery -->
<a id='snippet-sample_icompiledquery'></a>
```cs
public interface ICompiledQuery<TDoc, TOut>
{
    Expression<Func<IMartenQueryable<TDoc>, TOut>> QueryIs();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L13-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledquery' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In its simplest usage, let's say that we want to find the first user document with a certain first name. That class would look like this:

<!-- snippet: sample_FindByFirstName -->
<a id='snippet-sample_findbyfirstname'></a>
```cs
public class FindByFirstName : ICompiledQuery<User, User>
{
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
    {
        return q => q.FirstOrDefault(x => x.FirstName == FirstName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L134-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_findbyfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

So a couple things to note in the class above:

1. The `QueryIs()` method returns an Expression representing a Linq query
1. `FindByFirstName` has a property (it could also be just a public field) called `FirstName` that is used to express the filter of the query

To use the `FindByFirstName` query, just use the code below:

<!-- snippet: sample_using-compiled-query -->
<a id='snippet-sample_using-compiled-query'></a>
```cs
var justin = theSession.Query(new FindByFirstName {FirstName = "Justin"});

var tamba = await theSession.QueryAsync(new FindByFirstName {FirstName = "Tamba"});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L181-L185' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-compiled-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or to use it as part of a batched query, this syntax:

<!-- snippet: sample_batch-query-with-compiled-queries -->
<a id='snippet-sample_batch-query-with-compiled-queries'></a>
```cs
var batch = theSession.CreateBatchQuery();

var justin = batch.Query(new FindByFirstName {FirstName = "Justin"});
var tamba = batch.Query(new FindByFirstName {FirstName = "Tamba"});

await batch.Execute();

(await justin).Id.ShouldBe(user1.Id);
(await tamba).Id.ShouldBe(user2.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L149-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-query-with-compiled-queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


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

<!-- snippet: sample_ICompiledListQuery-with-no-select -->
<a id='snippet-sample_icompiledlistquery-with-no-select'></a>
```cs
public interface ICompiledListQuery<TDoc>: ICompiledListQuery<TDoc, TDoc>
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L26-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledlistquery-with-no-select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A sample usage of this type of query is shown below:

<!-- snippet: sample_UsersByFirstName-Query -->
<a id='snippet-sample_usersbyfirstname-query'></a>
```cs
public class UsersByFirstName: ICompiledListQuery<User>
{
    public static int Count;
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
    {
        return query => query.Where(x => x.FirstName == FirstName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/Compiled/compiled_query_Tests.cs#L406-L418' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usersbyfirstname-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you do want to use a `Select()` transform, use this interface:

<!-- snippet: sample_ICompiledListQuery-with-select -->
<a id='snippet-sample_icompiledlistquery-with-select'></a>
```cs
public interface ICompiledListQuery<TDoc, TOut>: ICompiledQuery<TDoc, IEnumerable<TOut>>
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L38-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledlistquery-with-select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A sample usage of this type of query is shown below:

<!-- snippet: sample_UserNamesForFirstName -->
<a id='snippet-sample_usernamesforfirstname'></a>
```cs
public class UserNamesForFirstName: ICompiledListQuery<User, string>
{
    public Expression<Func<IMartenQueryable<User>, IEnumerable<string>>> QueryIs()
    {
        return q => q
            .Where(x => x.FirstName == FirstName)
            .Select(x => x.UserName);
    }

    public string FirstName { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/Compiled/compiled_query_Tests.cs#L430-L443' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usernamesforfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for included documents

If you wish to use a compiled query for a document, using a `JOIN` so that the query will include another document, just as the (Include())(/guide/documents/querying/include) method does on a simple query, the compiled query would be constructed just like any other, using the `Include()` method
on the query:

<!-- snippet: sample_compiled_include -->
<a id='snippet-sample_compiled_include'></a>
```cs
[Fact]
public void simple_compiled_include_for_a_single_document()
{
    var user = new User();
    var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

    theSession.Store<object>(user, issue);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var issueQuery = new IssueByTitleWithAssignee {Title = issue.Title};
        var issue2 = query.Query(issueQuery);

        SpecificationExtensions.ShouldNotBeNull(issueQuery.Included);
        issueQuery.Included.Single().Id.ShouldBe(user.Id);

        SpecificationExtensions.ShouldNotBeNull(issue2);
    }
}

public class IssueByTitleWithAssignee : ICompiledQuery<Issue>
{
    public string Title { get; set; }
    public IList<User> Included { get; private set; } = new List<User>();

    public Expression<Func<IMartenQueryable<Issue>, Issue>> QueryIs()
    {
        return query => query
            .Include(x => x.AssigneeId, Included)
            .Single(x => x.Title == Title);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_compiled_include_Tests.cs#L24-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this example, the query has an `Included` property which will receive the included Assignee / `User`. The 'resulting' included property can only be
a property of the query, so that Marten would know how to assign the included result of the postgres query.
The `JoinType` property here is just an example for overriding the default `INNER JOIN`. If you wish to force an `INNER JOIN` within the query
you can simply remove the `JoinType` parameter like so: `.Include<Issue, IssueByTitleWithAssignee>(x => x.AssigneeId, x => x.Included)`

You can also chain `Include` methods if you need more than one `JOIN`s.

### Querying for multiple included documents

Fetching "included" documents could also be done when you wish to include multiple documents.
So picking up the same example, if you wish to get a list of `Issue`s and for every Issue you wish to retrieve
its' Assignee / `User`, in your compiled query you should have a list of `User`s like so:

<!-- snippet: sample_compiled_include_list -->
<a id='snippet-sample_compiled_include_list'></a>
```cs
public class IssueWithUsers : ICompiledListQuery<Issue>
{
    public List<User> Users { get; set; } = new List<User>();
    // Can also work like that:
    //public List<User> Users => new List<User>();

    public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
    {
        return query => query.Include(x => x.AssigneeId, Users);
    }
}

[Fact]
public void compiled_include_to_list()
{
    var user1 = new User();
    var user2 = new User();

    var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
    var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
    var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

    theSession.Store(user1, user2);
    theSession.Store(issue1, issue2, issue3);
    theSession.SaveChanges();

    using (var session = theStore.QuerySession())
    {
        var query = new IssueWithUsers();

        var issues = session.Query(query).ToArray();

        query.Users.Count.ShouldBe(2);
        issues.Count().ShouldBe(3);

        query.Users.Any(x => x.Id == user1.Id);
        query.Users.Any(x => x.Id == user2.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_compiled_include_Tests.cs#L61-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that you could either have the list instantiated or at least make sure the property has a setter as well as a getter (we've got your back).

As with the simple include queries, you could also use a Dictionary with a key type corresponding to the Id of the document- the dictionary value type:

<!-- snippet: sample_compiled_include_dictionary -->
<a id='snippet-sample_compiled_include_dictionary'></a>
```cs
public class IssueWithUsersById : ICompiledListQuery<Issue>
{
    public IDictionary<Guid,User> UsersById { get; set; } = new Dictionary<Guid, User>();
    // Can also work like that:
    //public List<User> Users => new Dictionary<Guid,User>();

    public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
    {
        return query => query.Include(x => x.AssigneeId, UsersById);
    }
}

[Fact]
public void compiled_include_to_dictionary()
{
    var user1 = new User();
    var user2 = new User();

    var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
    var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
    var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

    theSession.Store(user1, user2);
    theSession.Store(issue1, issue2, issue3);
    theSession.SaveChanges();

    using (var session = theStore.QuerySession())
    {
        var query = new IssueWithUsersById();

        var issues = session.Query(query).ToArray();

        issues.ShouldNotBeEmpty();

        query.UsersById.Count.ShouldBe(2);
        query.UsersById.ContainsKey(user1.Id).ShouldBeTrue();
        query.UsersById.ContainsKey(user2.Id).ShouldBeTrue();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_compiled_include_Tests.cs#L103-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include_dictionary' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for paginated results

Marten compiled queries also support queries for paginated results, where you could specify the page number and size, as well as getting the total count.
A simple example of how this can be achieved as follows:

<!-- snippet: sample_compiled-query-statistics -->
<a id='snippet-sample_compiled-query-statistics'></a>
```cs
public class TargetPaginationQuery: ICompiledListQuery<Target>
{
    public TargetPaginationQuery(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public int PageNumber { get; set; }
    public int PageSize { get; set; }

    public QueryStatistics Stats { get; } = new QueryStatistics();

    public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
    {
        return query => query
            .Where(x => x.Number > 10)
            .Skip(PageNumber)
            .Take(PageSize);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_statistics.cs#L23-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled-query-statistics' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the way to get the `QueryStatistics` out is done by having a property on the query, which we specify in the `Stats()` method, similarly to the way
we handle Include queries.

## Querying for a single document

If you are querying for a single document with no transformation, you can use this interface as a convenience:

<!-- snippet: sample_ICompiledQuery-for-single-doc -->
<a id='snippet-sample_icompiledquery-for-single-doc'></a>
```cs
public interface ICompiledQuery<TDoc>: ICompiledQuery<TDoc, TDoc>
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L49-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledquery-for-single-doc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And an example:

<!-- snippet: sample_FindUserByAllTheThings -->
<a id='snippet-sample_finduserbyallthethings'></a>
```cs
public class FindUserByAllTheThings: ICompiledQuery<User>
{
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
    {
        return query =>
                query.Where(x => x.FirstName == FirstName && Username == x.UserName)
                    .Where(x => x.LastName == LastName)
                    .Single();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/Compiled/compiled_query_Tests.cs#L286-L302' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_finduserbyallthethings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for multiple results as Json

To query for multiple results and have them returned as a Json string, you may run any query on your `IQueryable<T>` (be it ordering or filtering) and then simply finalize the query with `ToJsonArray();` like so:

<!-- snippet: sample_CompiledToJsonArray -->
<a id='snippet-sample_compiledtojsonarray'></a>
```cs
public class FindJsonOrderedUsersByUsername: ICompiledListQuery<User>
{
    public string FirstName { get; set; }

    Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> ICompiledQuery<User, IEnumerable<User>>.QueryIs()
    {
        return query =>
            query.Where(x => FirstName == x.FirstName)
                .OrderBy(x => x.UserName);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/Compiled/compiled_query_Tests.cs#L318-L333' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledtojsonarray' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you wish to do it asynchronously, you can use the `ToJsonArrayAsync()` method.

A sample usage of this type of query is shown below:

<[sample:sample_FindJsonOrderedUsersByUsername]>

Note that the result has the documents comma separated and wrapped in angle brackets (as per the Json notation).

## Querying for a single document as JSON

Finally, if you are querying for a single document as json, you will need to prepend your call to `Single()`, `First()` and so on with a call to `AsJson()`:

<!-- snippet: sample_CompiledAsJson -->
<a id='snippet-sample_compiledasjson'></a>
```cs
public class FindJsonUserByUsername: ICompiledQuery<User>
{
    public string Username { get; set; }

    Expression<Func<IMartenQueryable<User>, User>> ICompiledQuery<User, User>.QueryIs()
    {
        return query =>
            query.Where(x => Username == x.UserName).Single();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/Compiled/compiled_query_Tests.cs#L304-L316' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledasjson' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And an example:

<[sample:sample_FindJsonUserByUsername]>

(our `ToJson()` method simply returns a string representation of the `User` instance in Json notation)
