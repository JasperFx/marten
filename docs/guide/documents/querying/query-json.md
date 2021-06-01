# Query for Raw JSON

Added in Marten v0.6 is the ability to retrieve just the raw JSON string for a document. The point is to be able to fetch the raw JSON from the database for a document and immediately stream that data to a web client without having to take the performance hit of deserializing and serializing the object to and from JSON.

As of v0.6, Marten supplies the `IQuerySession/IDocumentSession.FindById<T>()` mechanism as shown below:

<!-- snippet: sample_find-json-by-id -->
<!-- endSnippet -->

There is also an asynchronous version:

<!-- snippet: sample_find-json-by-id-async -->
<!-- endSnippet -->

As of v0.9, Marten supplies the following functionality to retrieve the raw JSON strings:

<!-- snippet: sample_get-raw-json -->
<!-- endSnippet -->

And the asynchronous version:

<!-- snippet: sample_get-raw-json-async -->
<!-- endSnippet -->

## Using AsJson() with Select() Transforms

New for Marten v0.9.1 is the ability to combine the `AsJson()` mechanics to the result of a `Select()` transform:

<!-- snippet: sample_AsJson-plus-Select-1 -->
<!-- endSnippet -->

And another example, but this time transforming to an anonymous type:

<!-- snippet: sample_AsJson-plus-Select-2 -->
<!-- endSnippet -->
