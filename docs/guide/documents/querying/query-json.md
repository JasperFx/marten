# Query for Raw JSON

Added in Marten v0.6 is the ability to retrieve just the raw JSON string for a document. The point is to be able to fetch the raw JSON from the database for a document and immediately stream that data to a web client without having to take the performance hit of deserializing and serializing the object to and from JSON.

As of v0.6, Marten supplies the `IQuerySession/IDocumentSession.FindById<T>()` mechanism as shown below:

<!-- snippet: sample_find-json-by-id -->
<a id='snippet-sample_find-json-by-id'></a>
```cs
[Fact]
public void when_find_then_a_json_should_be_returned()
{
    var issue = new Issue { Title = "Issue 1" };

    theSession.Store(issue);
    theSession.SaveChanges();

    var json = theSession.Json.FindById<Issue>(issue.Id);
    json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"BugId\": null, \"Title\": \"Issue 1\", \"Number\": 0, \"AssigneeId\": null, \"ReporterId\": null}}");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/document_session_find_json_Tests.cs#L12-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_find-json-by-id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There is also an asynchronous version:

<!-- snippet: sample_find-json-by-id-async -->
<a id='snippet-sample_find-json-by-id-async'></a>
```cs
[Fact]
public async Task when_find_then_a_json_should_be_returned()
{
    var issue = new Issue { Title = "Issue 2" };

    theSession.Store(issue);
    await theSession.SaveChangesAsync();

    var json = await theSession.Json.FindByIdAsync<Issue>(issue.Id);
    json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"BugId\": null, \"Title\": \"Issue 2\", \"Number\": 0, \"AssigneeId\": null, \"ReporterId\": null}}");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/document_session_find_json_async_Tests.cs#L13-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_find-json-by-id-async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of v0.9, Marten supplies the following functionality to retrieve the raw JSON strings:

<!-- snippet: sample_get-raw-json -->
<a id='snippet-sample_get-raw-json'></a>
```cs
[Fact]
public async Task when_get_json_then_raw_json_should_be_returned()
{
    var issue = new Issue { Title = "Issue 1" };

    theSession.Store(issue);
    await theSession.SaveChangesAsync();
    var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArray();
    json.ShouldNotBeNull();

    json = await theSession.Query<Issue>().ToJsonFirst();
    json = await theSession.Query<Issue>().ToJsonFirstOrDefault();
    json = await theSession.Query<Issue>().ToJsonSingle();
    json = await theSession.Query<Issue>().ToJsonSingleOrDefault();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/get_raw_json_Tests.cs#L14-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get-raw-json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the asynchronous version:

<!-- snippet: sample_get-raw-json-async -->
<a id='snippet-sample_get-raw-json-async'></a>
```cs
[Fact]
public async Task when_get_json_then_raw_json_should_be_returned_async()
{
    var issue = new Issue { Title = "Issue 1" };

    theSession.Store(issue);
    await theSession.SaveChangesAsync();
    var json = await theSession.Query<Issue>().Where(x => x.Title == "Issue 1").ToJsonArray();
    json.ShouldNotBeNull();

    json = await theSession.Query<Issue>().ToJsonFirst();
    json = await theSession.Query<Issue>().ToJsonFirstOrDefault();
    json = await theSession.Query<Issue>().ToJsonSingle();
    json = await theSession.Query<Issue>().ToJsonSingleOrDefault();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/get_raw_json_Tests.cs#L32-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get-raw-json-async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Using AsJson() with Select() Transforms

New for Marten v0.9.1 is the ability to combine the `AsJson()` mechanics to the result of a `Select()` transform:

<!-- snippet: sample_AsJson-plus-Select-1 -->
<a id='snippet-sample_asjson-plus-select-1'></a>
```cs
var json = await theSession
    .Query<User>()
    .OrderBy(x => x.FirstName)

    // Transform the User class to a different type
    .Select(x => new UserName { Name = x.FirstName })
    .ToJsonFirst();

    json.ShouldBe("{\"Name\": \"Bill\"}");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/streaming_json_results.cs#L975-L985' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asjson-plus-select-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And another example, but this time transforming to an anonymous type:

<!-- snippet: sample_AsJson-plus-Select-2 -->
<a id='snippet-sample_asjson-plus-select-2'></a>
```cs
(await theSession
    .Query<User>()
    .OrderBy(x => x.FirstName)

    // Transform to an anonymous type
    .Select(x => new {Name = x.FirstName})

    // Select only the raw JSON
    .ToJsonFirstOrDefault())
     .ShouldBe("{\"Name\": \"Bill\"}");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/streaming_json_results.cs#L948-L960' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asjson-plus-select-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
