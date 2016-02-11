<!--Title:Document Hierarchies-->
<!--Url:hierarchies-->

Marten now allows you to specify that hierarchies of document types should be stored in one table and allow you
to query for either the base class or any of the subclasses.

To make that concrete, let's say you have a document type named `User` that has a pair of specialized subclasses
called `SuperUser` and `AdminUser`. To use the document hierarchy storage, we need to tell Marten that
`SuperUser` and `AdminUser` should just be stored as subclasses of `User` like this:

<[sample:configure-hierarchy-of-types]>

With the configuration above, you can now query by `User` and get `AdminUser` and `SuperUser` documents as part of the results,
or query directly for any of the subclasses to limit the query. 

The best description of what is possible with hierarchical storage is to read the [acceptance tests for this feature](https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs).

There's a couple things to be aware of with type hierarchies:

* A document type that is either abstract or an interface is automatically assumed to be a hierarchy
* If you want to use a concrete type as the base class for a hierarchy, you will need to explicitly configure
  that by adding the subclasses as shown above
* At this point, you can only specify "Searchable" fields on the top, base type
* The subclass document types must be convertable to the top level type. As of right now, Marten does not support "structural typing",
  but may in the future
* Internally, the subclass type documents are also stored as the parent type in the Identity Map mechanics. Many, many hours of
  banging my head on my desk were required to add this feature.