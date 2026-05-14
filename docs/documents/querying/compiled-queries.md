# Compiled Queries and Query Plans

Marten has two different implementations for the ["Specification" pattern](https://deviq.com/design-patterns/specification-pattern) that
enable you to encapsulate all the filtering, ordering, and paging for a logically reusable data query into a single class:

1. Compiled queries that help short circuit the LINQ processing with reusable "execution plans" for maximum performance, but
   are admittedly limited in terms of their ability to handle all possible LINQ queries or basically any dynamic querying whatsoever
2. Query plans that can support anything that Marten itself can do, just without the magic LINQ query compilation optimization

## Compiled Queries

::: warning
Don't use asynchronous Linq operators in the expression body of a compiled query. This will not impact your ability to use compiled queries
in asynchronous querying.
:::

::: warning
Compiled queries cannot use the recently added [primary constructor feature in C#](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors), and so far we don't even have a way to validate
when you are using this feature in compiled query planning. Be warned.
:::

Linq is easily one of the most popular features in .Net and arguably the one thing that other platforms strive to copy. We generally like being able
to express document queries in compiler-safe manner, but there is a non-trivial cost in parsing the resulting [Expression trees](https://msdn.microsoft.com/en-us/library/bb397951.aspx) and then using plenty of string concatenation to build up the matching SQL query.

Fortunately, Marten supports the concept of a _Compiled Query_ that you can use to reuse the SQL template for a given Linq query and bypass the performance cost of continuously parsing Linq expressions.

All compiled queries are classes that implement the `ICompiledQuery<TDoc, TResult>` interface shown below:

<!-- snippet: sample_icompiledquery -->
<a id='snippet-sample_icompiledquery'></a>
```cs
public interface ICompiledQuery<TDoc, TOut> : ICompiledQueryMarker where TDoc: notnull
{
    Expression<Func<IMartenQueryable<TDoc>, TOut>> QueryIs();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L29-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledquery' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In its simplest usage, let's say that we want to find the first user document with a certain first name. That class would look like this:

<!-- snippet: sample_findbyfirstname -->
<a id='snippet-sample_findbyfirstname'></a>
```cs
public class FindByFirstName: ICompiledQuery<User, User>
{
    public string FirstName { get; set; }

    public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
    {
        return q => q.FirstOrDefault(x => x.FirstName == FirstName);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L96-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_findbyfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
There are many more example compiled query classes in the [acceptance tests for compiled queries](https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs) within the Marten codebase.
:::

So a couple things to note in the class above:

1. The `QueryIs()` method returns an Expression representing a Linq query
1. `FindByFirstName` has a property (it could also be just a public field) called `FirstName` that is used to express the filter of the query

To use the `FindByFirstName` query, just use the code below:

<!-- snippet: sample_using-compiled-query -->
<a id='snippet-sample_using-compiled-query'></a>
```cs
var justin = await session.QueryAsync(new FindByFirstName { FirstName = "Justin" });

var tamba = await session.QueryAsync(new FindByFirstName { FirstName = "Tamba" });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L135-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-compiled-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or to use it as part of a batched query, this syntax:

<!-- snippet: sample_batch-query-with-compiled-queries -->
<a id='snippet-sample_batch-query-with-compiled-queries'></a>
```cs
var batch = session.CreateBatchQuery();

var justin = batch.Query(new FindByFirstName { FirstName = "Justin" });
var tamba = batch.Query(new FindByFirstName { FirstName = "Tamba" });

await batch.Execute();

(await justin).Id.ShouldBe(user1.Id);
(await tamba).Id.ShouldBe(user2.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/BatchedQuerying/batched_querying_acceptance_Tests.cs#L115-L127' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch-query-with-compiled-queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Recommended setup: enable the source generator <Badge type="tip" text="9.0" />

Marten 9 ships a Roslyn source generator (`Marten.SourceGenerator`) that emits the per-`ICompiledQuery<,>` scaffolding at compile time instead of synthesizing it via Roslyn at first call. Opt-in is implicit — add two things to the project that declares your compiled query types and the runtime picks the source-generated path automatically:

```xml
<!-- in your .csproj -->
<ItemGroup>
    <PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />
</ItemGroup>
```

```cs
// in any file in the same assembly (e.g., AssemblyInfo.cs)
[assembly: JasperFx.JasperFxAssembly]
```

With both present, the generator emits a typed handler for every `ICompiledQuery<TDoc, TOut>` in the assembly and registers it with the Marten runtime via a `[ModuleInitializer]` that fires at assembly load. **You do not need to call `StoreOptions.RegisterCompiledQueryType(...)`** — that API exists for the legacy runtime-codegen pre-build flow described in [Pre-Building Generated Types](/configuration/prebuilding) and is no longer required when the source generator is in play.

What this gets you compared to the legacy runtime codegen:

* **No Roslyn emit at first call.** First invocation of a registered compiled query is dramatically faster (PoC measurements: ~25× on a Stateless shape — 3 ms vs 75 ms on a warm host).
* **No `FastExpressionCompiler` for the per-query parameter binder.** The generator emits a direct property-read switch — ~31% faster steady-state per call.
* **AOT-publishable for the common cases.** The runtime Roslyn dependency disappears for queries the source-gen path covers.

The runtime falls back to the legacy code-generation path in three documented cases — these are bug-compatible with Marten 8 behavior, just slower at cold start:

* Plans whose SQL needs an `ICompiledQueryAwareFilter` (string `Contains`/`StartsWith`/`EndsWith`, `HashSet<T>.Contains` with JSONB containment, `Dictionary<,>.ContainsKey`, child-collection JsonPath counts).
* Generic or nested `ICompiledQuery<,>` types — the generator skips both shapes for now.
* Compiled queries declared in an assembly without `[JasperFxAssembly]`.

The source generator is additive — adding it never breaks an existing compiled query, and removing it makes everything fall back to runtime codegen with no behavior change beyond cold-start perf. Tracking issue: [#4405](https://github.com/JasperFx/marten/issues/4405).

## How Does It Work?

The first time that Marten encounters a new type of `ICompiledQuery`, it builds a "plan" for the query by:

1. Finding all public _readable_ properties or fields on the compiled query type that would be potential parameters. Members marked with the `[MartenIgnore]` attribute are skipped.
2. Either confirming that the query object you passed in has unique values across each parameter member, or constructing a new instance of the same type and assigning unique values itself.
3. Parsing the expression returned from `QueryIs()` to derive the SQL command + the parameter slots Marten needs to fill at execution time.
4. Matching the unique member values back to the command's parameter values to map query members to database parameters by index.

How Marten then dispatches the per-call hot path depends on whether the source generator is in play:

* **Source-generated dispatch** (default when the `Marten.SourceGenerator` analyzer reference + `[assembly: JasperFxAssembly]` are present, see [Recommended setup](#recommended-setup-enable-the-source-generator) above). The generator emits a typed `{Query}_CompiledQueryHandler` class for every `ICompiledQuery<,>` in the assembly, plus a `[ModuleInitializer]` that registers the handler with Marten's runtime `CompiledQueryHandlerRegistry` at assembly load. When Marten sees the registered query type, it routes through the generator-emitted parameter binder (direct field/property reads — no reflection, no Roslyn emit at runtime).
* **Runtime-codegen dispatch** (legacy fallback). When the source generator isn't opted in, or when the plan requires features the source-gen path doesn't yet cover (see the bullet list under [Recommended setup](#recommended-setup-enable-the-source-generator)), Marten falls back to the pre-9.0 behavior: dynamically generates and compiles a per-query handler with Roslyn on first call and caches it. The mechanics are documented at [Pre-Building Generated Types](/configuration/prebuilding) — that flow stays available as the AOT escape hatch when the source generator can't cover a particular shape.

On subsequent calls to the same compiled query type, both paths just reuse the cached handler — the SQL command + parameter binder are remembered for the life of the `DocumentStore`.

You may need to help Marten out a little bit with the compiled query support in determining unique parameter values to use
during query planning by implementing the new `Marten.Linq.IQueryPlanning` interface on your compiled query type. Consider this
example query that uses paging:

<!-- snippet: sample_implementing_iqueryplanning -->
<a id='snippet-sample_implementing_iqueryplanning'></a>
```cs
public class CompiledTimeline : ICompiledListQuery<TimelineItem>, IQueryPlanning
{
    public int PageSize { get; set; } = 20;

    [MartenIgnore] public int Page { private get; set; } = 1;
    public int SkipCount => (Page - 1) * PageSize;
    public string Type { get; set; }
    public Expression<Func<IMartenQueryable<TimelineItem>, IEnumerable<TimelineItem>>> QueryIs() =>
        query => query.Where(i => i.Event == Type).Skip(SkipCount).Take(PageSize);

    public void SetUniqueValuesForQueryPlanning()
    {
        Page = 3; // Setting Page to 3 forces the SkipCount and PageSize to be different values
        PageSize = 20; // This has to be a positive value, or the Take() operator has no effect
        Type = Guid.NewGuid().ToString();
    }

    // And hey, if you have a public QueryStatistics member on your compiled
    // query class, you'll get the total number of records
    public QueryStatistics Statistics { get; } = new QueryStatistics();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_1891_compiled_query_problem.cs#L25-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_implementing_iqueryplanning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Pay close attention to the `SetUniqueValuesForQueryPlanning()` method. That has absolutely no other purpose but to help Marten
create a compiled query plan for the `CompiledTimeline` type.

## What is Supported?

To the best of our knowledge and testing, you may use any [Linq feature that Marten supports](/documents/querying/linq/) within a compiled query. So any combination of:

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
* `Skip()`, `Take()` and `Stats()` for pagination

As for limitations,

* You cannot use the Linq `ToArray()` or `ToList()` operators. See the next section for an explanation of how to query for multiple results with `ICompiledListQuery`.
* The compiled query planning just cannot match Boolean fields or properties to command arguments, so Boolean flags cannot be used
* You cannot use any asynchronous operators. So in all cases, use the synchronous operator equivalent. So `FirstOrDefault()`, but not `FirstOrDefaultAsync()`. **This does not preclude you from using compiled queries in asynchronous querying**

## Querying for Multiple Results

To query for multiple results, you need to just return the raw `IQueryable<T>` as `IEnumerable<T>` as the result type. You cannot use the `ToArray()` or `ToList()` operators (it'll throw exceptions from the Relinq library if you try). As a convenience mechanism, Marten supplies these helper interfaces:

If you are selecting the whole document without any kind of `Select()` transform, you can use this interface:

<!-- snippet: sample_icompiledlistquery-with-no-select -->
<a id='snippet-sample_icompiledlistquery-with-no-select'></a>
```cs
public interface ICompiledListQuery<TDoc>: ICompiledListQuery<TDoc, TDoc> where TDoc : notnull
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L44-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledlistquery-with-no-select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A sample usage of this type of query is shown below:

<!-- snippet: sample_usersbyfirstname-query -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L589-L602' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usersbyfirstname-query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you do want to use a `Select()` transform, use this interface:

<!-- snippet: sample_icompiledlistquery-with-select -->
<a id='snippet-sample_icompiledlistquery-with-select'></a>
```cs
public interface ICompiledListQuery<TDoc, TOut>: ICompiledQuery<TDoc, IEnumerable<TOut>> where TDoc : notnull
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L58-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledlistquery-with-select' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A sample usage of this type of query is shown below:

<!-- snippet: sample_usernamesforfirstname -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L614-L628' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usernamesforfirstname' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for Related Documents with Include()

If you wish to use a compiled query for a document, using a `JOIN` so that the query will include another document, just as the [`Include()`](/documents/querying/linq/include) method does on a simple query, the compiled query would be constructed just like any other, using the `Include()` method
on the query:

<!-- snippet: sample_compiled_include -->
<a id='snippet-sample_compiled_include'></a>
```cs
[Fact]
public async Task simple_compiled_include_for_a_single_document()
{
    var user = new User();
    var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

    using var session = theStore.IdentitySession();
    session.Store<object>(user, issue);
    await session.SaveChangesAsync();

    using var query = theStore.QuerySession();
    var issueQuery = new IssueByTitleWithAssignee { Title = issue.Title };
    var issue2 = await query.QueryAsync(issueQuery);

    issueQuery.Included.ShouldNotBeNull();
    issueQuery.Included.Single().Id.ShouldBe(user.Id);

    issue2.ShouldNotBeNull();
}

public class IssueByTitleWithAssignee: ICompiledQuery<Issue>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_compiled_include.cs#L16-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this example, the query has an `Included` property which will receive the included Assignee / `User`. The 'resulting' included property can only be
a property of the query, so that Marten would know how to assign the included result of the postgres query.
The `JoinType` property here is just an example for overriding the default `INNER JOIN`. If you wish to force an `INNER JOIN` within the query
you can simply remove the `JoinType` parameter like so: `.Include<Issue, IssueByTitleWithAssignee>(x => x.AssigneeId, x => x.Included)`

You can also chain `Include` methods if you need more than one `JOIN`s.

## Querying for Multiple Related Documents

Fetching "included" documents could also be done when you wish to include multiple documents.
So picking up the same example, if you wish to get a list of `Issue`s and for every Issue you wish to retrieve
its' Assignee / `User`, in your compiled query you should have a list of `User`s like so:

<!-- snippet: sample_compiled_include_list -->
<a id='snippet-sample_compiled_include_list'></a>
```cs
public class IssueWithUsers: ICompiledListQuery<Issue>
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
public async Task compiled_include_to_list()
{
    var user1 = new User();
    var user2 = new User();

    var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
    var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
    var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

    using var session = theStore.IdentitySession();
    session.Store(user1, user2);
    session.Store(issue1, issue2, issue3);
    await session.SaveChangesAsync();

    using var querySession = theStore.QuerySession();
    var compiledQuery = new IssueWithUsers();

    var issues = await querySession.QueryAsync(compiledQuery);

    compiledQuery.Users.Count.ShouldBe(2);
    issues.Count().ShouldBe(3);

    compiledQuery.Users.Any(x => x.Id == user1.Id);
    compiledQuery.Users.Any(x => x.Id == user2.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_compiled_include.cs#L53-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that you could either have the list instantiated or at least make sure the property has a setter as well as a getter (we've got your back).

As with the simple include queries, you could also use a Dictionary with a key type corresponding to the Id of the document- the dictionary value type:

<!-- snippet: sample_compiled_include_dictionary -->
<a id='snippet-sample_compiled_include_dictionary'></a>
```cs
public class IssueWithUsersById: ICompiledListQuery<Issue>
{
    public IDictionary<Guid, User> UsersById { get; set; } = new Dictionary<Guid, User>();
    // Can also work like that:
    //public List<User> Users => new Dictionary<Guid,User>();

    public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
    {
        return query => query.Include(x => x.AssigneeId, UsersById);
    }
}

[Fact]
public async Task compiled_include_to_dictionary()
{
    var user1 = new User();
    var user2 = new User();

    var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
    var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
    var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

    using var session = theStore.IdentitySession();
    session.Store(user1, user2);
    session.Store(issue1, issue2, issue3);
    await session.SaveChangesAsync();

    using var querySession = theStore.QuerySession();
    var compiledQuery = new IssueWithUsersById();

    var issues = await querySession.QueryAsync(compiledQuery);

    issues.ShouldNotBeEmpty();

    compiledQuery.UsersById.Count.ShouldBe(2);
    compiledQuery.UsersById.ContainsKey(user1.Id).ShouldBeTrue();
    compiledQuery.UsersById.ContainsKey(user2.Id).ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_compiled_include.cs#L96-L137' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled_include_dictionary' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for Paginated Results

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/statistics_and_paged_list.cs#L64-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiled-query-statistics' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the way to get the `QueryStatistics` out is done by having a property on the query, which we specify in the `Stats()` method, similarly to the way
we handle Include queries.

## Querying for a Single Document

If you are querying for a single document with no transformation, you can use this interface as a convenience:

<!-- snippet: sample_icompiledquery-for-single-doc -->
<a id='snippet-sample_icompiledquery-for-single-doc'></a>
```cs
public interface ICompiledQuery<TDoc>: ICompiledQuery<TDoc, TDoc> where TDoc : notnull
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Linq/ICompiledQuery.cs#L71-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_icompiledquery-for-single-doc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And an example:

<!-- snippet: sample_finduserbyallthethings -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L377-L394' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_finduserbyallthethings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying for Multiple Results as JSON

To query for multiple results and have them returned as a Json string, you may run any query on your `IQueryable<T>` (be it ordering or filtering) and then simply finalize the query with `ToJsonArray();` like so:

<!-- snippet: sample_compiledtojsonarray -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L411-L425' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledtojsonarray' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you wish to do it asynchronously, you can use the `ToJsonArrayAsync()` method.

A sample usage of this type of query is shown below:

<!-- snippet: sample_compiledtojsonarray -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L411-L425' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledtojsonarray' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the result has the documents comma separated and wrapped in angle brackets (as per the Json notation).

## Querying for a Single Document as JSON

Finally, if you are querying for a single document as json, you will need to prepend your call to `Single()`, `First()` and so on with a call to `AsJson()`:

<!-- snippet: sample_compiledasjson -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L396-L409' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledasjson' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And an example:

<!-- snippet: sample_compiledasjson -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L396-L409' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_compiledasjson' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

(our `ToJson()` method simply returns a string representation of the `User` instance in Json notation)

## Using with QueryStatistics

Compiled queries can be used with the `QueryStatistics` paging helper. You just need to have a public member
on your compiled query class of type `QueryStatistics` with a value. Marten will do the rest and use that
object to collect the total number of rows in the database when the query is executed. Here's an example
from the Marten tests:

<!-- snippet: sample_targetsinorder -->
<a id='snippet-sample_targetsinorder'></a>
```cs
public class TargetsInOrder: ICompiledListQuery<Target>
{
    // This is all you need to do
    public QueryStatistics Statistics { get; } = new QueryStatistics();

    public int PageSize { get; set; } = 20;
    public int Start { get; set; } = 5;

    Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> ICompiledQuery<Target, IEnumerable<Target>>.
        QueryIs()
    {
        return q => q
            .OrderBy(x => x.Id).Skip(Start).Take(PageSize);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L525-L543' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_targetsinorder' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And when used in the actual test:

<!-- snippet: sample_using_querystatistics_with_compiled_query -->
<a id='snippet-sample_using_querystatistics_with_compiled_query'></a>
```cs
[Fact]
public async Task use_compiled_query_with_statistics()
{
    await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
    var targets = Target.GenerateRandomData(100).ToArray();
    await theStore.BulkInsertAsync(targets);

    var query = new TargetsInOrder { PageSize = 10, Start = 20 };

    var results = await theSession.QueryAsync(query);

    // Verifying that the total record count in the database matching
    // the query is determined when this is executed
    query.Statistics.TotalResults.ShouldBe(100);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Compiled/compiled_queries.cs#L38-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_querystatistics_with_compiled_query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Query Plans <Badge type="tip" text="7.25" />

::: info
The query plan concept was created specifically to help a [JasperFx](https://jasperfx.net) client try to eliminate their
custom repository wrappers around Marten and to better utilize batch querying.
:::

::: tip
Batch querying is a great way to improve the performance of your system~~~~
:::

A query plan is another flavor of "Specification" for Marten that just enables you to bundle up query logic that can
be reused within your codebase without having to create wrappers around Marten itself. To create a reusable query plan,
implement the `IQueryPlan<T>` interface where `T` is the type of the result you want. Here's a simplistic sample from
the tests:

<!-- snippet: sample_color_targets -->
<a id='snippet-sample_color_targets'></a>
```cs
public class ColorTargets: QueryListPlan<Target>
{
    public Colors Color { get; }

    public ColorTargets(Colors color)
    {
        Color = color;
    }

    // All we're doing here is just turning around and querying against the session
    // All the same though, this approach lets you do much more runtime logic
    // than a compiled query can
    public override IQueryable<Target> Query(IQuerySession session)
    {
        return session.Query<Target>().Where(x => x.Color == Color).OrderBy(x => x.Number);
    }
}

// The above is short hand for:

public class LonghandColorTargets: IQueryPlan<IReadOnlyList<Target>>, IBatchQueryPlan<IReadOnlyList<Target>>
{
    public Colors Color { get; }

    public LonghandColorTargets(Colors color)
    {
        Color = color;
    }

    public Task<IReadOnlyList<Target>> Fetch(IQuerySession session, CancellationToken token)
    {
        return session
            .Query<Target>()
            .Where(x => x.Color == Color)
            .OrderBy(x => x.Number)
            .ToListAsync(token: token);
    }

    public Task<IReadOnlyList<Target>> Fetch(IBatchedQuery batch)
    {
        return batch
            .Query<Target>()
            .Where(x => x.Color == Color)
            .OrderBy(x => x.Number)
            .ToList();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_plans.cs#L74-L124' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_color_targets' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And then use that like so:

<!-- snippet: sample_using_query_plan -->
<a id='snippet-sample_using_query_plan'></a>
```cs
public static async Task use_query_plan(IQuerySession session, CancellationToken token)
{
    var targets = await session
        .QueryByPlanAsync(new ColorTargets(Colors.Blue), token);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_plans.cs#L128-L136' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_query_plan' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There is also a similar interface for usage with [batch querying](/documents/querying/batched-queries):

<!-- snippet: sample_ibatchqueryplan -->
<a id='snippet-sample_ibatchqueryplan'></a>
```cs
/// <summary>
/// Marten's concept of the "Specification" pattern for reusable
/// queries within Marten batched queries. Use this for operations that cannot be supported by Marten compiled queries
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IBatchQueryPlan<T>
{
    Task<T> Fetch(IBatchedQuery query);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/IQueryPlan.cs#L21-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ibatchqueryplan' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And because we expect this to be very common, there is convenience base class named `QueryListPlan<T>` for querying lists of `T` data that can be used for both querying directly against an `IQuerySession` and for batch querying. The usage within a batched query is shown below from the Marten tests:

<!-- snippet: sample_using_query_plan_in_batch_query -->
<a id='snippet-sample_using_query_plan_in_batch_query'></a>
```cs
[Fact]
public async Task use_as_batch()
{
    await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

    var targets = Target.GenerateRandomData(1000).ToArray();
    await theStore.BulkInsertDocumentsAsync(targets);

    // Start a batch query
    var batch = theSession.CreateBatchQuery();

    // Using the ColorTargets plan twice, once for "Blue" and once for "Green" target documents
    var blueFetcher = batch.QueryByPlan(new ColorTargets(Colors.Blue));
    var greenFetcher = batch.QueryByPlan(new ColorTargets(Colors.Green));

    // Execute the batch query
    await batch.Execute();

    // The batched querying in Marten is essentially registering a "future"
    // for each query, so we'll await each task from above to get at the actual
    // data returned from batch.Execute() above
    var blues = await blueFetcher;
    var greens = await greenFetcher;

    // And the assertion part of our arrange, act, assertion test
    blues.ShouldNotBeEmpty();
    greens.ShouldNotBeEmpty();

    var expectedBlues = targets.Where(x => x.Color == Colors.Blue).OrderBy(x => x.Number);
    var expectedReds = targets.Where(x => x.Color == Colors.Green).OrderBy(x => x.Number);

    blues.Select(x => x.Id).ShouldBe(expectedBlues.Select(x => x.Id));
    greens.Select(x => x.Id).ShouldBe(expectedReds.Select(x => x.Id));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_plans.cs#L34-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_query_plan_in_batch_query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
