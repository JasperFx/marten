# Custom Identity Strategies

A custom ID generator strategy should implement [IIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/IIdGeneration.cs).

<!-- snippet: sample_custom-id-generation -->
<a id='snippet-sample_custom-id-generation'></a>
```cs
public class CustomdIdGeneration : IIdGeneration
{
    public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

    public bool RequiresSequences { get; } = false;
    public void GenerateCode(GeneratedMethod assign, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        assign.Frames.Code($"_setter({{0}}, \"newId\");", document);
        assign.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L14-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-id-generation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Build()` method should return the actual `IdGenerator<T>` for the document type, where `T` is the type of the Id field.

For more advances examples you can have a look at existing ID generator: [HiloIdGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/HiloIdGeneration.cs), [CombGuidGenerator](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/CombGuidIdGeneration.cs) and the [IdentityKeyGeneration](https://github.com/JasperFx/marten/blob/master/src/Marten/Schema/Identity/Sequences/IdentityKeyGeneration.cs),

To use custom id generation you should enabled it when configuring the document store. This defines that the strategy will be used for all the documents types.

<!-- snippet: sample_configuring-global-custom -->
<a id='snippet-sample_configuring-global-custom'></a>
```cs
options.Policies.ForAllDocuments(m =>
{
    if (m.IdType == typeof(string))
    {
        m.IdStrategy = new CustomdIdGeneration();
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L37-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible define a custom id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-custom -->
<a id='snippet-sample_configuring-mapping-specific-custom'></a>
```cs
options.Schema.For<UserWithString>().IdStrategy(new CustomdIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CustomKeyGenerationTests.cs#L68-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-custom' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
