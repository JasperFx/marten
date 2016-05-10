<!--Title:Query for Raw JSON-->
<!--Url:query_json-->

Added in Marten v0.6 is the ability to retrieve just the raw JSON string for a document. The point is to be able to fetch the raw JSON from the database for a document and immediately stream that data to a web client without having to take the performance hit of deserializing and serializing the object to and from JSON.

As of v0.6, Marten supplies the `IQuerySession/IDocumentSession.FindById<T>()` mechanism as shown below:

<[sample:find-json-by-id]>

There is also an asynchronous version:

<[sample:find-json-by-id-async]>

As of v0.9, Marten supplies the following functionality to retrieve the raw JSON strings:

<[sample:get-raw-json]>

And the asynchronous version:

<[sample:get-raw-json-async]>

## Using AsJson() with Select() Transforms

New for Marten v0.9.1 is the ability to combine the `AsJson()` mechanics to the result of a `Select()` transform:

<[sample:AsJson-plus-Select-1]>

And another example, but this time transforming to an anonymous type:

<[sample:AsJson-plus-Select-2]>