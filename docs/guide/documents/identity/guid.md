# Guid Identifiers

::: tip INFO
As of Marten 1.0-alpha, the default Guid mechanism is a sequential or "Comb" Guid. While more expensive to
generate, this makes inserts into the underlying document tables more efficient.
:::

## CombGuid

To use _CombGuid_ generation you should enabled it when configuring the document store. This defines that the _CombGuid_ generation strategy will be used for all the documents types.

<!-- snippet: sample_configuring-global-sequentialguid -->
<a id='snippet-sample_configuring-global-sequentialguid'></a>
```cs
options.Policies.ForAllDocuments(m =>
{
    if (m.IdType == typeof(Guid))
    {
        m.IdStrategy = new CombGuidIdGeneration();
    }
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CombGuidIdGenerationTests.cs#L43-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<!-- snippet: sample_configuring-mapping-specific-sequentialguid -->
<a id='snippet-sample_configuring-mapping-specific-sequentialguid'></a>
```cs
options.Schema.For<UserWithGuid>().IdStrategy(new CombGuidIdGeneration());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/CombGuidIdGenerationTests.cs#L76-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-mapping-specific-sequentialguid' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
