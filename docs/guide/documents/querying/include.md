# Including Related Documents

## Join a Single Document

Marten supports the ability to run include queries that execute a `join` SQL query behind the curtains, in order to fetch a referenced document as well as the queried document. Suppose you are querying for a github `Issue` that contains a property `AssigneeId`, which references the Id of the `User` assigned to the Issue. If you wish to fetch the `User` as well in one trip to the database, you can use the `.Include()` method like so:

<!-- snippet: sample_simple_include -->
<a id='snippet-sample_simple_include'></a>
```cs
[Fact]
public void simple_include_for_a_single_document()
{
    var user = new User();
    var issue = new Issue {AssigneeId = user.Id, Title = "Garage Door is busted"};

    theSession.Store<object>(user, issue);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        User included = null;
        var issue2 = query
            .Query<Issue>()
            .Include<User>(x => x.AssigneeId, x => included = x)
            .Single(x => x.Title == issue.Title);

        SpecificationExtensions.ShouldNotBeNull(included);
        included.Id.ShouldBe(user.Id);

        SpecificationExtensions.ShouldNotBeNull(issue2);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#L86-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The first parameter of the `Include()` method takes an expression that specifies the document properties on which the join will be done (`AssigneeId` in this case). The second parameter is the expression that will assign the fetched related document to a previously declared variable (`included` in our case). By default, Marten will use an inner join. This means that any `Issue` with no corresponding `User` (or no `AssigneeId`), will not be fetched. If you wish to override this behaviour, you can add as a third parameter the enum `JoinType.LeftOuter`.

## Join Many Documents

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

    theSession.Store(user1, user2);
    theSession.Store(issue1, issue2, issue3);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var dict = new Dictionary<Guid, User>();

        query.Query<Issue>().Include(x => x.AssigneeId, dict).ToArray();

        dict.Count.ShouldBe(2);
        dict.ContainsKey(user1.Id).ShouldBeTrue();
        dict.ContainsKey(user2.Id).ShouldBeTrue();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#L490-L516' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_dictionary_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Include Multiple Document Types

Marten also allows you to chain multiple `Include()` calls:

<!-- snippet: sample_multiple_include -->
<a id='snippet-sample_multiple_include'></a>
```cs
[Fact]
public void multiple_includes()
{
    var assignee = new User();
    var reporter = new User();

    var issue1 = new Issue { AssigneeId = assignee.Id, ReporterId = reporter.Id, Title = "Garage Door is busted" };

    theSession.Store(assignee, reporter);
    theSession.Store(issue1);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        User assignee2 = null;
        User reporter2 = null;

        query
                .Query<Issue>()
                .Include<User>(x => x.AssigneeId, x => assignee2 = x)
                .Include<User>(x => x.ReporterId, x => reporter2 = x)
                .Single()
                .ShouldNotBeNull();

        assignee2.Id.ShouldBe(assignee.Id);
        reporter2.Id.ShouldBe(reporter.Id);

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#L713-L743' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multiple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Chaining other Linq Methods

Marten supports chaining other linq methods to allow more complex quries such as:

* `Where()`
* `OrderBy()`
* `OrderByDescending()`

## Asynchronous Support

Marten supports Include within an asynchronous context. The query will be run asynchronously when you append your query with the corresponding Async method, like:

* `ToListAsync()`
* `SingleAsync()`

And so on...

Marten also supports running an Include query within [batched queries](/guide/documents/querying/batched-queries):

<!-- snippet: sample_batch_include -->
<a id='snippet-sample_batch_include'></a>
```cs
var batch = query.CreateBatchQuery();

var found = batch.Query<Issue>()
    .Include<User>(x => x.AssigneeId, x => included = x)
    .Where(x => x.Title == issue1.Title)
    .Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#L48-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
