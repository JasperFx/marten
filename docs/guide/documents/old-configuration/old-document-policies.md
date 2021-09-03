# Document Policies

Document Policies enable convention-based customizations to be applied across the Document Store. While Marten has some existing policies that can be enabled, any custom policy can be introduced  through implementing the `IDocumentPolicy` interface and applying it on `StoreOptions.Policies` or through using the `Policies.ForAllDocuments(Action<DocumentMapping> configure)` shorthand.

The following sample demonstrates a policy that sets types implementing `IRequireMultiTenancy` marker-interface to be multi-tenanted (see [tenancy](/guide/documents/tenancy/)).

<!-- snippet: sample_sample-policy-configure -->
<a id='snippet-sample_sample-policy-configure'></a>
```cs
var store = DocumentStore.For(storeOptions =>
{
    // Apply custom policy
    storeOptions.Policies.OnDocuments<TenancyPolicy>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L19-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-configure' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_sample-policy-implementation -->
<a id='snippet-sample_sample-policy-implementation'></a>
```cs
public interface IRequireMultiTenancy
{
}

public class TenancyPolicy: IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.DocumentType.GetInterfaces().Any(x => x == typeof(IRequireMultiTenancy)))
        {
            mapping.TenancyStyle = TenancyStyle.Conjoined;
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Policies.cs#L31-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-policy-implementation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To set all types to be multi-tenanted, the pre-baked `Policies.AllDocumentsAreMultiTenanted` could also have been used.

Remarks: Given the sample, you might not want to let tenancy concerns propagate to your types in a real data model.
