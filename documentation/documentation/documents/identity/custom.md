<!--Title:Custom Identity Strategies-->

A custom ID generator strategy should implement [IIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/IIdGeneration.cs).

<[sample:custom-id-generation]>

The `Build()` method should return the actual `IdGenerator<T>` for the document type, where `T` is the type of the Id field.

For more advances examples you can have a look at existing ID generator: [HiloIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/HiloIdGeneration.cs), [CombGuidGenerator](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs) and the [IdentityKeyGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/IdentityKeyGeneration.cs), 

To use custom id generation you should enabled it when configuring the document store. This defines that the strategy will be used for all the documents types.

<[sample:configuring-global-custom]>

It is also possible define a custom id generation algorithm for a specific document type.

<[sample:configuring-mapping-specific-custom]>

