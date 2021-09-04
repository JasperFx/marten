# Loading Documents by Id

Documents can be loaded by id from the `IQuerySession` interface (and so also `IDocumentSession`), either one at a time or by an enumerable of id values. The load by id functionality supports GUIDs, integers, long integers, and strings. If the document cannot be found, `null` is returned.

## Synchronous Loading

<!-- snippet: sample_load_by_id -->
<a id='snippet-sample_load_by_id'></a>
```cs
public void LoadById(IDocumentSession session)
{
    var userId = Guid.NewGuid();

    // Load a single document identified by a Guid
    var user = session.Load<User>(userId);

    // There's an overload of Load for integers and longs
    var doc = session.Load<IntDoc>(15);

    // Another overload for documents identified by strings
    var doc2 = session.Load<StringDoc>("Hank");

    // Load multiple documents by a group of id's
    var users = session.LoadMany<User>(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    // If you already have an array of id values
    var users2 = session.LoadMany<User>(ids);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Load_by_Id.cs#L10-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_load_by_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Asynchronous Loading

<!-- snippet: sample_async_load_by_id -->
<a id='snippet-sample_async_load_by_id'></a>
```cs
public async Task LoadByIdAsync(IQuerySession session, CancellationToken token = default (CancellationToken))
{
    var userId = Guid.NewGuid();

    // Load a single document identified by a Guid
    var user = await session.LoadAsync<User>(userId, token);

    // There's an overload of Load for integers and longs
    var doc = await session.LoadAsync<IntDoc>(15, token);

    // Another overload for documents identified by strings
    var doc2 = await session.LoadAsync<StringDoc>("Hank", token);

    // Load multiple documents by a group of ids
    var users = await session.LoadManyAsync<User>(token, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    // If you already have an array of id values
    var users2 = await session.LoadManyAsync<User>(token, ids);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Load_by_Id.cs#L35-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_load_by_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
