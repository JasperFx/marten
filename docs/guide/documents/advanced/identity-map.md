# Identity Map Mechanics

**Identity Map:**

> Ensures that each object gets loaded only once by keeping every loaded object in a map. Looks up objects using the map when referring to them.
>
>-- <cite>[Martin Fowler](http://martinfowler.com/eaaCatalog/identityMap.html)</cite>

Marten's `IDocumentSession` implements the [_Identity Map_](https://en.wikipedia.org/wiki/Identity_map_pattern) pattern that seeks to cache documents loaded by id. This behavior can be very valuable, for example, in handling web requests or service bus messages when many different objects or functions may need to access the same logical document. Using the identity map mechanics allows the application to easily share data and avoid the extra database access hits -- as long as the `IDocumentSession` is scoped to the web request.

<!-- snippet: sample_using-identity-map -->
<a id='snippet-sample_using-identity-map'></a>
```cs
public void using_identity_map()
{
    var user = new User { FirstName = "Tamba", LastName = "Hali" };
    theStore.BulkInsert(new[] { user });

    // Open a document session with the identity map
    using (var session = theStore.OpenSession())
    {
        session.Load<User>(user.Id)
            .ShouldBeTheSameAs(session.Load<User>(user.Id));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IdentityMapTests.cs#L8-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-identity-map' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that using the identity map functionality can be wasteful if you aren't able to take advantage of the identity map caching in a session. In those cases, you may want to either use the `IDocumentStore.LightweightSession()` which forgos the identity map functionality, or use the read only `IQuerySession` alternative. RavenDb users will note that Marten does not (yet) support any notion of `Evict()` to manually remove documents from identity map tracking to avoid memory usage problems. Our hope is that the existence of the lightweight session and the read only interface will alleviate the memory explosion problems that you can run into with naive usage of identity maps or the dirty checking when fetching a large number of documents.

The Identity Map functionality is applied to all documents loaded by Id or Linq queries with `IQuerySession/IDocumentSession.Query<T>()`. **Documents loaded by user-supplied SQL in the `IQuerySession.Query<T>(sql)` mechanism bypass the Identity Map functionality.**
