<!--title:Document Id's-->

Besides being serializable, Marten's only other requirement for a .Net type to be a document is the existence of an identifier field or property that Marten can use as the primary key for the document type. The `Id` can be either a public field or property, and the name must be either `id` or `Id`. As of this time, Marten supports these `Id` types:

1. `String`. It might be valuable to use a [natural key](https://en.wikipedia.org/wiki/Natural_key) as the identifier, especially if it is valuable within the 
   <[linkto:documentation/documents/identitymap;title=Identity Map]> feature of Marten Db. In this case, the user will 
   be responsible for supplying the identifier.
1. `Guid`. If the id is a Guid, Marten will assign a new value for you when you persist the document for the first time if the id is empty. 
   _And for the record, it's pronounced "gwid"_.
1. `CombGuid` is a [sequential Guid algorithm](https://en.wikipedia.org/wiki/Globally_unique_identifier#Sequential_algorithms). It can improve performance over the default Guid as it reduces fragmentation of the PK index. (More info soon)
1. `Int` or `Long`. As of right now, Marten uses a [HiLo generator](http://stackoverflow.com/questions/282099/whats-the-hi-lo-algorithm) approach to assigning numeric identifiers by document type. 
   Marten may support Postgresql sequences or star-based algorithms as later alternatives.
1. When the ID member of a document is not settable or not-public a `NoOpIdGeneration` strategy is used. This ensures that Marten does not set the ID itself, so the ID should be generated manually.
1. A `Custom` ID generator strategy is used to implement the ID generation strategy yourself.

You can see some example id usages below:

<[sample:id_samples]>

## Hilo Sequences

The _Hilo_ sequence generation can be customized with either global defaults or document type specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

To set different global defaults, use the `StoreOptions.HiloSequenceDefaults` property like this sample:

<[sample:configuring-global-hilo-defaults]>

To override the Hilo configuration for a specific document type, you can decorate the document type with the `[HiloSequence]` attribute
as in this example:

<[sample:overriding-hilo-with-attribute]>

You can also use the `MartenRegistry` fluent interface to override the Hilo configuration for a document type as in this example:

<[sample:overriding-hilo-with-marten-registry]>

## CombGuid

To use _CombGuid_ generation you should enabled it when configuring the document store. This defines that the _CombGuid_ generation strategy will be used for all the documents types.

<[sample:configuring-global-sequentialguid]>

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<[sample:configuring-mapping-specific-sequentialguid]>

## Custom

A custom ID generator strategy should implement [IIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/IIdGeneration.cs).

<[sample:custom-id-generation]>

The AssignmentBodyCode method should return the C# code that assigns the value of the Id member.

For more advances examples you can have a look at existing ID generator: [HiloIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Sequences/HiloIdGeneration.cs), [CombGuidGenerator](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/CombGuidIdGeneration.cs) and the [IdentityKeyGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Sequences/IdentityKeyGeneration.cs), 

To use custom id generation you should enabled it when configuring the document store. This defines that the strategy will be used for all the documents types.

<[sample:configuring-global-custom]>

It is also possible define a custom id generation algorithm for a specific document type.

<[sample:configuring-mapping-specific-custom]>





