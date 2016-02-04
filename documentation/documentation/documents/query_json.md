<!--Title:Query for Raw JSON-->
<!--Url:query_json-->

<div class="alert alert-info" role="alert">The Marten team will be adding more functionality in the future to retrieve the raw JSON strings for the results of projections and aggregations.</div>

Added in Marten v0.6 is the ability to retrieve just the raw JSON string for a document. The point is to be able to fetch the raw JSON from the database for a document and immediately stream that data to a web client without having to take the performance hit of deserializing and serializing the object to and from JSON.

As of v0.6, Marten supplies the `IQuerySession/IDocumentSession.FindById<T>()` mechanism as shown below:

<[sample:find-json-by-id]>

There is also an asynchronous version:

<[sample:find-json-by-id-async]>
