# Loading Documents by Id

Documents can be loaded by id from the `IQuerySession` interface (and so also `IDocumentSession`), either one at a time or by an enumerable of id values. The load by id functionality supports GUIDs, integers, long integers, and strings. If the document cannot be found, `null` is returned.

## Loading by Id

<!-- snippet: sample_load_by_id -->
<a id='snippet-sample_load_by_id'></a>
```cs
public async Task LoadById(IDocumentSession session)
{
    var userId = Guid.NewGuid();

    // Load a single document identified by a Guid
    var user = await session.LoadAsync<User>(userId);

    // There's an overload of Load for integers and longs
    var doc = await session.LoadAsync<IntDoc>(15);

    // Another overload for documents identified by strings
    var doc2 = await session.LoadAsync<StringDoc>("Hank");

    // Load multiple documents by a group of id's
    var users = await session.LoadManyAsync<User>(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    // If you already have an array of id values
    var users2 = await session.LoadManyAsync<User>(ids);
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

## Batched Loading and the Shared Contract

Every `LoadManyAsync()` overload above is a **single** round trip, however many ids you pass — Marten
sends `where id = ANY(:ids)` rather than a query per id, so there is no parameter ceiling to work around
and no benefit to chunking the list yourself.

That also holds when you reach Marten through `IDocumentReadOperations`, the store-agnostic read contract
shared with the other Critter Stack stores:

<!-- snippet: sample_load_many_through_the_shared_contract -->
<a id='snippet-sample_load_many_through_the_shared_contract'></a>
```cs
public static async Task<IReadOnlyList<Reservation>> LoadAll(
    JasperFx.Events.Documents.IDocumentReadOperations session,
    IEnumerable<Guid> ids,
    CancellationToken token)
{
    // Store-agnostic, and on Marten this is a single round trip.
    // Ids with no document are left out, and a repeated id comes back once.
    return await session.LoadManyAsync<Reservation>(ids, token);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/BatchReads.cs#L50-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_load_many_through_the_shared_contract' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Ids with no document are left out of the result, a repeated id yields its document once, and the order of
the returned list is **not** part of the contract — sort it yourself if you need one.

::: tip
The contract ships a default implementation that loads one document per round trip, and Marten overrides
it. If you are writing store-agnostic code, the semantics above are the same on every store; only the
round-trip count differs.
:::
