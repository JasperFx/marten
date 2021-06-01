# Loading Documents by Id

Documents can be loaded by id from the `IQuerySession` interface (and so also `IDocumentSession`), either one at a time or by an enumerable of id values. The load by id functionality supports GUIDs, integers, long integers, and strings. If the document cannot be found, `null` is returned.

## Synchronous Loading

<!-- snippet: sample_load_by_id -->
<!-- endSnippet -->

## Asynchronous Loading

<!-- snippet: sample_async_load_by_id -->
<!-- endSnippet -->
