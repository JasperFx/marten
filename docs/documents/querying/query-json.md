# Query for Raw JSON

Marten stores documents as JSON, and sometimes it might be valuable to access the raw JSON representation of the stored documents. To that end, the `IQuerySession.Json` property gives you access to several helper methods to load JSON strings.

<!-- snippet: sample_find-json-by-id -->
<a id='snippet-sample_find-json-by-id'></a>
```cs
[Fact]
public async Task when_find_then_a_json_should_be_returned()
{
    var issue = new Issue { Title = "Issue 2" };

    theSession.Store(issue);
    await theSession.SaveChangesAsync();

    var json = await theSession.Json.FindByIdAsync<Issue>(issue.Id);
    json.ShouldBe($"{{\"Id\": \"{issue.Id}\", \"Tags\": null, \"BugId\": null, \"Title\": \"Issue 2\", \"Number\": 0, \"Status\": null, \"AssigneeId\": null, \"ReporterId\": null}}");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Json/document_session_find_json_Tests.cs#L12-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_find-json-by-id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten supplies the following functionality to retrieve the raw JSON strings:

<!-- snippet: sample_get-raw-json -->
<a id='snippet-sample_get-raw-json'></a>
```cs
[Fact]
public async Task when_get_json_then_raw_json_should_be_returned()
{
    // Its sibling already does this. Both tests store an "Issue 1" and then call
    // ToJsonSingle(), which requires exactly one match, so whichever runs second
    // fails without a reset.
    await theStore.Advanced.ResetAllData();

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Json/get_raw_json_Tests.cs#L13-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get-raw-json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Raw JSON from Select() Transforms

The `ToJsonArray()`, `ToJsonFirst()`, `ToJsonFirstOrDefault()`, `ToJsonSingle()` and `ToJsonSingleOrDefault()` operators also work on the result of a `Select()` transform:

::: tip
When a `Select()` projection is a "simple" flat member-access transform, Marten pushes the
whole reshaping down into a single `jsonb_build_object(...)` SQL expression instead of
deserializing the full document on the client, which is what makes the raw JSON results
below possible without an extra serialization round trip. See
[Projection Operators](/documents/querying/linq/projections) for the details and its
limitations.
:::

<!-- snippet: sample_asjson-plus-select-1 -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Json/streaming_json_results.cs#L1009-L1019' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asjson-plus-select-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And another example, but this time transforming to an anonymous type:

<!-- snippet: sample_asjson-plus-select-2 -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Json/streaming_json_results.cs#L982-L994' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asjson-plus-select-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Streaming Raw JSON

The `ToJson*()` methods above build a `string`. To write the raw JSON straight to a `Stream`, such as an HTTP response
body, without building the string or deserializing any documents, use the streaming forms:

<!-- snippet: sample_streaming_raw_json -->
<a id='snippet-sample_streaming_raw_json'></a>
```cs
// Every document that matches, as one JSON array. Returns how many documents were written
var count = await session.Query<User>()
    .Where(x => x.LastName == "Miller")
    .OrderBy(x => x.FirstName)
    .StreamJsonArray(destination, token);

// The first matching document. Writes nothing and returns false when no document matches
var found = await session.Query<User>()
    .Where(x => x.UserName == "jdm")
    .StreamJsonFirstOrDefault(destination, token);

// One document by its id. Returns false when there is none
found = await session.Json.StreamById<User>(userId, destination, token);

// The results of your own SQL, as one JSON array
count = await session.StreamJson<User>(destination, token,
    "where data ->> 'LastName' = ?", "Miller");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Json/StreamingJsonSamples.cs#L18-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_streaming_raw_json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- `StreamJsonArray()`, `StreamJsonFirst()`, `StreamJsonFirstOrDefault()`, `StreamJsonSingle()` and
  `StreamJsonSingleOrDefault()` work on any Marten LINQ query, including one with a `Select()` transform.
- `session.Json.StreamById<T>()` writes one document by its id.
- `session.StreamJson<T>()` writes the results of your own SQL. See [Querying with Postgres SQL](/documents/querying/sql).
- `StreamPagedJsonArray()` writes one page of results with its paging metadata. See [Paging](/documents/querying/linq/paging).
- `StreamJsonOne()` and `StreamJsonMany()` stream the results of a [compiled query](/documents/querying/compiled-queries).

In ASP.NET Core, the [Marten.AspNetCore](/documents/aspnetcore) package wraps these up to write to the
`HttpResponse` directly, along with the status code and content type.
