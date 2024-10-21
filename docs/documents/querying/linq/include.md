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
public async Task simple_include_for_a_single_document()
{
    var user = new User();
    var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

    using var session = theStore.IdentitySession();
    session.Store<object>(user, issue);
    await session.SaveChangesAsync();

    using var query = theStore.QuerySession();
    query.Logger = new TestOutputMartenLogger(_output);

    User included = null;
    var issue2 = query
        .Query<Issue>()
        .Include<User>(x => included = x).On(x => x.AssigneeId)
        .Single(x => x.Title == issue.Title);

    included.ShouldNotBeNull();
    included.Id.ShouldBe(user.Id);

    issue2.ShouldNotBeNull();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L80-L107' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Include()` method takes an expression that will assign the fetched related document to a previously declared variable (`included` in our case). The `Include()` method should then be followed by the `On()` method (named after sql `LEFT JOIN ... ON ...`). The first parameter of `On()` method takes an expression that specifies the document properties on which the join will be done (`AssigneeId` in this case).

Marten will use the equivalent of a left join. This means that any `Issue` with no corresponding `User` (or no `AssigneeId`) will still be fetched, just with no matching user.

## Include Many Documents

If you wish to fetch a list of related documents, you can declare a `List<User>` variable and pass it as the second parameter. The `Include()` method should be appended with `ToList()` or `ToArray()`.

Instead of a List, you could also use a Dictionary with a key type corresponding to the Id type and a value type corresponding to the Document type:

<!-- snippet: sample_dictionary_include -->
<a id='snippet-sample_dictionary_include'></a>
```cs
[Fact]
public async Task include_to_dictionary()
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

    using var query = theStore.QuerySession();
    var dict = new Dictionary<Guid, User>();

    query.Query<Issue>().Include(dict).On(x => x.AssigneeId).ToArray();

    dict.Count.ShouldBe(2);
    dict.ContainsKey(user1.Id).ShouldBeTrue();
    dict.ContainsKey(user2.Id).ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L473-L500' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_dictionary_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Filtering included documents

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
        .Include(list).On(x => x.TargetId, t => t.Color == Colors.Blue)
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
public async Task multiple_includes()
{
    var assignee = new User{FirstName = "Assignee"};
    var reporter = new User{FirstName = "Reporter"};

    var issue1 = new Issue { AssigneeId = assignee.Id, ReporterId = reporter.Id, Title = "Garage Door is busted" };

    using var session = theStore.IdentitySession();
    session.Store(assignee, reporter);
    session.Store(issue1);
    await session.SaveChangesAsync();

    using var query = theStore.QuerySession();
    User assignee2 = null;
    User reporter2 = null;

    query.Logger = new TestOutputMartenLogger(_output);
    query
        .Query<Issue>()
        .Include<User>(x => assignee2 = x).On(x => x.AssigneeId)
        .Include<User>(x => reporter2 = x).On(x => x.ReporterId)
        .Single()
        .ShouldNotBeNull();

    assignee2.Id.ShouldBe(assignee.Id);
    reporter2.Id.ShouldBe(reporter.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L730-L761' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multiple_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Mapping to documents on any property

By default, documents are included based on a value that maps to the related document's `Id`/`[Identity]` property. It is also possible to map related documents on any property of that document which allows for much more flexible joins.

<!-- snippet: sample_include_using_custom_map -->
<a id='snippet-sample_include_using_custom_map'></a>
```cs
[Fact]
public async Task include_using_custom_map()
{
    var classroom = new Classroom(Id: Guid.NewGuid(), RoomCode: "Classroom-1A");
    var user = new SchoolUser(Id: Guid.NewGuid(), Name: "Student #1", HomeRoom: "Classroom-1A");

    using var session = theStore.IdentitySession();
    session.Store<object>(classroom, user);
    await session.SaveChangesAsync();

    using var query = theStore.QuerySession();
    Classroom? included = null;

    var user2 = query
        .Query<SchoolUser>()
        .Include<Classroom>(c => included = c).On(u => u.HomeRoom, c => c.RoomCode)
        .Single(u => u.Name == "Student #1");

    included.ShouldNotBeNull();
    included.Id.ShouldBe(classroom.Id);
    user2.ShouldNotBeNull();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L935-L960' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_include_using_custom_map' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By joining on a value other than the document id, this opens up the possibility of one-to-many joins, with potentially many related documents matching the queried document. Using a list as described above will allow for all matching records to be returned.  Alternatively you can also use a dictionary of lists, where the key is the Id type and the value is an `IList` of a type corresponding to the Document type:

<!-- snippet: sample_dictionary_list_include -->
<a id='snippet-sample_dictionary_list_include'></a>
```cs
[Fact]
public async Task include_to_dictionary_list()
{
    var class1 = new Classroom(Id: Guid.NewGuid(), RoomCode: "Classroom-1A");
    var class2 = new Classroom(Id: Guid.NewGuid(), RoomCode: "Classroom-2B");

    var user1 = new SchoolUser(Id: Guid.NewGuid(), Name: "Student #1", HomeRoom: "Classroom-1A");
    var user2 = new SchoolUser(Id: Guid.NewGuid(), Name: "Student #2", HomeRoom: "Classroom-2B");
    var user3 = new SchoolUser(Id: Guid.NewGuid(), Name: "Student #3", HomeRoom: "Classroom-2B");

    using var session = theStore.IdentitySession();
    session.Store(class1, class2);
    session.Store(user1, user2, user3);
    await session.SaveChangesAsync();

    using var query = theStore.QuerySession();
    var dict = new Dictionary<string, IList<SchoolUser>>();

    var classes = query
        .Query<Classroom>()
        .Include(dict).On(c => c.RoomCode, u => u.HomeRoom)
        .ToArray();

    classes.Length.ShouldBe(2);
    dict.Count.ShouldBe(2);
    dict.ContainsKey(class1.RoomCode).ShouldBeTrue();
    dict.ContainsKey(class2.RoomCode).ShouldBeTrue();
    dict[class1.RoomCode].Count.ShouldBe(1);
    dict[class2.RoomCode].Count.ShouldBe(2);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L962-L995' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_dictionary_list_include' title='Start of snippet'>anchor</a></sup>
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
    .Include<User>(x => included = x).On(x => x.AssigneeId)
    .Where(x => x.Title == issue1.Title)
    .Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Includes/end_to_end_query_with_include.cs#L42-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_batch_include' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
