<!--title:Identity Map Mechanics-->

_Ensures that each object gets loaded only once by keeping every loaded object in a map. Looks up objects using the map when referring to them._

[Martin Fowler](http://martinfowler.com/eaaCatalog/identityMap.html)


Marten's `IDocumentSession` implements the "Identity Map" pattern that seeks to cache documents loaded by id. This behavior can be very valuable, for example, in handling web requests or service bus messages when many different objects or functions may need to access the same logical document. Using the identity map mechanics allows the application to easily share data and avoid the extra database access hits -- as long as the `IDocumentSession` is scoped to the web request.

<[sample:using-identity-map]>

Do note that using the identity map functionality can be wasteful if you aren't able to take advantage of the identity map caching in a session. In those cases, you may want to either use the `IDocumentStore.LightweightSession()` which forgos the identity map functionality, or use the read only `IQuerySession` alternative. RavenDb users will note that Marten does not (yet) support any notion of `Evict()` to manually remove documents from identity map tracking to avoid memory usage problems. Our hope is that the existence of the lightweight session and the read only interface will alleviate the memory explosion problems that you can run into with naive usage of identity maps or the dirty checking when fetching a large number of documents. 


**As of right now, Marten is only applying the Identity Map functionality to documents loaded by Id and misses documents fetched from `Query<T>` methods.
This [will be addressed soon](https://github.com/JasperFx/Marten/issues/88).**
