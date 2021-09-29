# Marten.AspNetCore 

::: tip
For a little more context, see the blog post [Efficient Web Services with Marten V4](https://jeremydmiller.com/2021/09/28/efficient-web-services-with-marten-v4/).
:::

In Marten V4, there is a small new addon that adds helpers for ASP.Net Core development, expressly
the ability to very efficiently _stream_ the raw JSON of persisted documents straight to an HTTP response
without every having to waste time with deserialization/serialization or even reading the data into a JSON
string in memory.

First, to get started, Marten provides **Marten.AspNetCore** plugin.

Install it through the [Nuget package](https://www.nuget.org/packages/Marten.AspNetCore/).

```powershell
PM> Install-Package Marten.AspNetCore
```

## Single Document

If you need to write a single Marten document to the HTTP response by its id, the most
efficient way is this syntax shown in a small sample MVC Core controller method:

<!-- snippet: sample_write_single_document_by_id_to_httpresponse -->
<a id='snippet-sample_write_single_document_by_id_to_httpresponse'></a>
```cs
[HttpGet("/issue/{issueId}")]
public Task Get(Guid issueId, [FromServices] IQuerySession session)
{
    // This "streams" the raw JSON to the HttpResponse
    // w/o ever having to read the full JSON string or
    // deserialize/serialize within the HTTP request
    return session.Json
        .WriteById<Issue>(issueId, HttpContext);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L39-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_write_single_document_by_id_to_httpresponse' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

That syntax will write the HTTP `content-type` and `content-length` response headers
as you'd expect, and copy the raw JSON for the document to the `HttpResponse.Body` stream
if the document is found. The status code will be 200 if the document is found, and 404 if
it is not.

Likewise, if you need to write a single document from a Linq query, you have this syntax:

<!-- snippet: sample_use_linq_to_write_single_document_to_httpcontext -->
<a id='snippet-sample_use_linq_to_write_single_document_to_httpcontext'></a>
```cs
[HttpGet("/issue2/{issueId}")]
public Task Get2(Guid issueId, [FromServices] IQuerySession session)
{
    return session.Query<Issue>().Where(x => x.Id == issueId)
        .WriteSingle(HttpContext);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L53-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_linq_to_write_single_document_to_httpcontext' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Multiple Documents

The `WriteArray()` extension method will allow you to write an array of documents in
a Linq query to the outgoing HTTP response like this:

<!-- snippet: sample_writing_multiple_documents_to_httpcontext -->
<a id='snippet-sample_writing_multiple_documents_to_httpcontext'></a>
```cs
[HttpGet("/issue/open")]
public Task OpenIssues([FromServices] IQuerySession session)
{
    // This "streams" the raw JSON to the HttpResponse
    // w/o ever having to read the full JSON string or
    // deserialize/serialize within the HTTP request
    return session.Query<Issue>()
        .Where(x => x.Open)
        .WriteArray(HttpContext);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L76-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_writing_multiple_documents_to_httpcontext' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Compiled Query Support

The absolute fastest way to invoke querying in Marten is by using [compiled queries](/guide/documents/querying/compiled-queries)
that allow you to use Linq queries without the runtime overhead of continuously 
parsing Linq expressions every time.

Back to the sample endpoint above where we write an array of all the open issues. We 
can express the same query in a simple compiled query like this:

<!-- snippet: sample_OpenIssues -->
<a id='snippet-sample_openissues'></a>
```cs
public class OpenIssues: ICompiledListQuery<Issue>
{
    public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
    {
        return q => q.Where(x => x.Open);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L102-L112' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_openissues' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And use that in an MVC Controller method like this:

<!-- snippet: sample_using_compiled_query_with_json_streaming -->
<a id='snippet-sample_using_compiled_query_with_json_streaming'></a>
```cs
[HttpGet("/issue2/open")]
public Task OpenIssues2([FromServices] IQuerySession session)
{
    return session.WriteArray(new OpenIssues(), HttpContext);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L91-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_compiled_query_with_json_streaming' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Likewise, you *could* use a compiled query to write a single document. As a contrived
sample, here's an example compiled query that reads a single `Issue` document by its
id:

<!-- snippet: sample_IssueById -->
<a id='snippet-sample_issuebyid'></a>
```cs
public class IssueById: ICompiledQuery<Issue, Issue>
{
    public Expression<Func<IMartenQueryable<Issue>, Issue>> QueryIs()
    {
        return q => q.FirstOrDefault(x => x.Id == Id);
    }

    public Guid Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L114-L126' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_issuebyid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the usage of that to write JSON directly to the `HttpContext` in a controller method:

<!-- snippet: sample_write_single_document_to_httpcontext_with_compiled_query -->
<a id='snippet-sample_write_single_document_to_httpcontext_with_compiled_query'></a>
```cs
[HttpGet("/issue3/{issueId}")]
public Task Get3(Guid issueId, [FromServices] IQuerySession session)
{
    return session.Query<Issue>().Where(x => x.Id == issueId)
        .WriteSingle(HttpContext);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Controllers/IssueController.cs#L64-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_write_single_document_to_httpcontext_with_compiled_query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

