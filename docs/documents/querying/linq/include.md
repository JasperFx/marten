# Including Related Documents

## Include a Single Document

::: tip
If you're interested, this functionality does not use SQL `JOIN` clauses, and has not since
the V4 release.
:::

Marten supports the ability to run include queries that make a single database call in order to fetch a referenced document as well as the queried document. Suppose you are querying for a github `Issue` that contains a property `AssigneeId`, which references the Id of the `User` assigned to the Issue. If you wish to fetch the `User` as well in one trip to the database, you can use the `.Include()` method like so:

<!-- snippet: sample_simple_include -->
<a id='snippet-sample_simple_include'></a>
```cs
[Fact]
public void simple_include_for_a_single_document()
{
    var user = new User();
    var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

    using var session = TheStore.IdentitySession();
    session.Store<object>(user, issue);
    session.SaveChanges();

    using var query = TheStore.QuerySession();
    query.Logger = new TestOutputMartenLogger(_output);

    User included = null;
    var issue2 = query
        .Query<Issue>()
        .Include<User>(x => x.AssigneeId, x => included = x)
        .Single(x => x.Title == issue.Title);

    included.ShouldNotBeNull();
    included.Id.ShouldBe(user.Id);

    issue2.ShouldNotBeNull();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L81-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The first parameter of the `Include()` method takes an expression that specifies the document properties on which the join will be done (`AssigneeId` in this case). The second parameter is the expression that will assign the fetched related document to a previously declared variable (`included` in our case). By default, Marten will use an inner join. This means that any `Issue` with no corresponding `User` (or no `AssigneeId`), will not be fetched. If you wish to override this behavior, you can add as a third parameter the enum `JoinType.LeftOuter`.

## Include Many Documents

If you wish to fetch a list of related documents, you can declare a `List<User>` variable and pass it as the second parameter. The `Include()` method should be appended with `ToList()` or `ToArray()`.

Instead of a List, you could also use a Dictionary with a key type corresponding to the Id type and a value type corresponding to the Document type:

<!-- snippet: sample_dictionary_include -->
<a id='snippet-sample_dictionary_include'></a>
```cs
[Fact]
public void include_to_dictionary()
{
    var user1 = new User();
    var user2 = new User();

    var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
    var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
    var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

    using var session = TheStore.IdentitySession();
    session.Store(user1, user2);
    session.Store(issue1, issue2, issue3);
    session.SaveChanges();

    using var query = TheStore.QuerySession();
    var dict = new Dictionary<Guid, User>();

    query.Query<Issue>().Include(x => x.AssigneeId, dict).ToArray();

    dict.Count.ShouldBe(2);
    dict.ContainsKey(user1.Id).ShouldBeTrue();
    dict.ContainsKey(user2.Id).ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L474-L501' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_dictionary_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of Marten V7, you can also filter the included documents in case of large data sets by
supplying an extra filter argument on the included document type (essentially a `Where()` clause on just the
included documents) like so:

<!-- snippet: sample_filter_included_documents -->
<a id='snippet-sample_filter_included_documents'></a>
```cs
[Fact]
public async Task filter_included_documents_to_lambda()
{
    var list = new List<Target>();

    var holders = await theSession.Query<TargetHolder>()
        .Include<Target>(x => x.TargetId, x => list.Add(x), t => t.Color == Colors.Blue)
        .ToListAsync();

    list.Select(x => x.Color).Distinct()
        .Single().ShouldBe(Colors.Blue);

    list.Count.ShouldBe(Data.Count(x => x.Color == Colors.Blue));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/includes_with_filtering_on_included_documents.cs#L49-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_filter_included_documents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Include Multiple Document Types

::: warning
Marten can only filter the included documents, not sort them. You would have to 
apply ordering in memory if so desired.
:::

Marten also allows you to chain multiple `Include()` calls:

<!-- snippet: sample_multiple_include -->
<a id='snippet-sample_multiple_include'></a>
```cs
[Fact]
public void multiple_includes()
{
    var assignee = new User{FirstName = "Assignee"};
    var reporter = new User{FirstName = "Reporter"};

    var issue1 = new Issue { AssigneeId = assignee.Id, ReporterId = reporter.Id, Title = "Garage Door is busted" };

    using var session = TheStore.IdentitySession();
    session.Store(assignee, reporter);
    session.Store(issue1);
    session.SaveChanges();

    using var query = TheStore.QuerySession();
    User assignee2 = null;
    User reporter2 = null;

    query.Logger = new TestOutputMartenLogger(_output);
    query
        .Query<Issue>()
        .Include<User>(x => x.AssigneeId, x => assignee2 = x)
        .Include<User>(x => x.ReporterId, x => reporter2 = x)
        .Single()
        .ShouldNotBeNull();

    assignee2.Id.ShouldBe(assignee.Id);
    reporter2.Id.ShouldBe(reporter.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L695-L726' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multiple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Asynchronous Support

Marten supports Include within an asynchronous context. The query will be run asynchronously when you append your query with the corresponding Async method, like:

* `ToListAsync()`
* `SingleAsync()`

And so on...

Marten also supports running an Include query within [batched queries](/documents/querying/batched-queries):

<!-- snippet: sample_batch_include -->
<a id='snippet-sample_batch_include'></a>
```cs
var batch = query.CreateBatchQuery();

var found = batch.Query<Issue>()
    .Include<User>(x => x.AssigneeId, x => included = x)
    .Where(x => x.Title == issue1.Title)
    .Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L43-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
