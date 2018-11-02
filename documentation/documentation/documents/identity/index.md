<!--title:Document Identity-->

Besides being serializable, Marten's only other requirement for a .Net type to be a document is the existence of an identifier field or property that Marten can use as the primary key for the document type. The `Id` can be either a public field or property, and the name must be either `id` or `Id` or `ID`. As of this time, Marten supports these `Id` types:

1. `String`. It might be valuable to use a [natural key](https://en.wikipedia.org/wiki/Natural_key) as the identifier, especially if it is valuable within the 
   <[linkto:documentation/documents/advanced/identitymap;title=Identity Map]> feature of Marten Db. In this case, the user will 
   be responsible for supplying the identifier.
1. `Guid`. If the id is a Guid, Marten will assign a new value for you when you persist the document for the first time if the id is empty. 
   _And for the record, it's pronounced "gwid"_.
1. `CombGuid` is a [sequential Guid algorithm](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs). It can improve performance over the default Guid as it reduces fragmentation of the PK index.
1. `Int` or `Long`. As of right now, Marten uses a [HiLo generator](http://stackoverflow.com/questions/282099/whats-the-hi-lo-algorithm) approach to assigning numeric identifiers by document type. 
   Marten may support Postgresql sequences or star-based algorithms as later alternatives.
1. When the ID member of a document is not settable or not-public a `NoOpIdGeneration` strategy is used. This ensures that Marten does not set the ID itself, so the ID should be generated manually.
1. A `Custom` ID generator strategy is used to implement the ID generation strategy yourself.

Marten by default uses the identity value set on documents and only assigns one in case it has no value (`Guid.Empty`, `0`, `string.Empty` etc).

<div class="alert alert-info">When using a `Guid`/`CombGuid`, `Int`, or `Long` identifier, Marten will ensure the identity is set immediately after calling `IDocumentSession.Store` on the entity.</div>

See these topics for more information about specific Id types:

<[TableOfContents]>

You can see some example id usages below:

<[sample:id_samples]>

## Overriding the Choice of Id Property/Field

If you really want to -- or you're migrating existing document types from another document database -- Marten provides
the `[Identity]` attribute to force Marten to use a property or field as the identifier that doesn't match
the "id" or "Id" or "ID" convention:

<[sample:IdentityAttribute]>

The identity property or field can also be configured through `StoreOptions` by using the `Schema` to obtain a document mapping:

<[sample:sample-override-id-fluent-interance]>

