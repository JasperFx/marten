<!--Title:Document Policies-->

Document Policies enable convention-based customizations to be applied across the Document Store. While Marten has some existing policies that can be enabled, any custom policy can be introduced  through implementing the `IDocumentPolicy` interface and applying it on `StoreOptions.Policies` or through using the `Policies.ForAllDocuments(Action<DocumentMapping> configure)` shorthand.

The following sample demonstrates a policy that sets types implementing `IRequireMultiTenancy` marker-interface to be multi-tenanted (see <[linkto:documentation/documents/tenancy]>. 

<[sample:sample-policy-configure]>

<[sample:sample-policy-implementation]>

To set all types to be multi-tenanted, the prebaked `Policies.AllDocumentsAreMultiTenanted` could also have been used.


Remarks: Given the sample, you might not want to let tenancy concerns propagate to your types in a real data model.