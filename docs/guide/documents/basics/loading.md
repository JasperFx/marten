# Loading Documents by Id

Documents can be loaded by id from the `IQuerySession` interface (and so also `IDocumentSession`), either one at a time or by an enumerable of id values. The load by id functionality supports GUIDs, integers, long integers, and strings. If the document cannot be found, `null` is returned.

## Synchronous Loading

<<< @/../src/Marten.Testing/Examples/Load_by_Id.cs#sample_load_by_id

## Asynchronous Loading

<<< @/../src/Marten.Testing/Examples/Load_by_Id.cs#sample_async_load_by_id
