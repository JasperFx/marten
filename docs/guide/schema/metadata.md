# Document and Event Metadata

::: tip INFO
Marten's metadata tracking abilities were greatly expanded in the v4.0 release.
:::

When Marten generates a table for document storage it now adds several _metadata_ columns
that further describe the document:

1. `mt_last_modified` - a timestamp of the last time the document was modified
1. `mt_dotnet_type` - The `FullName` property of the actual .Net type persisted. This is strictly for information and is not used by Marten itself.
1. `mt_version` - A sequential Guid designating the revision of the document. Marten uses
   this column in its optimistic concurrency checks
1. `mt_doc_type` - document name (_document <[linkto:documentation/documents/advanced/hierarchies;title=hierarchies]> only_)
1. `mt_deleted` - a boolean flag representing deleted state (_<[linkto:documentation/documents/advanced/soft_deletes;title=soft deletes]> only_)
1. `mt_deleted_at` - a timestamp of the time the document was deleted (_<[linkto:documentation/documents/advanced/soft_deletes;title=soft deletes]> only_)

## Finding the Metadata for a Document

::: warning
This method moved from `IDocumentStore.Advanced` to `IDocumentSession` in Marten v4.0.
:::

You can find the metadata values for a given document object with the following mechanism
on `IDocumentSession`:

<!-- snippet: sample_resolving_metadata -->
<a id='snippet-sample_resolving_metadata'></a>
```cs
[Fact]
public void hit_returns_values()
{
    var shop = new CoffeeShop();

    using (var session = theStore.OpenSession())
    {
        session.Store(shop);
        session.SaveChanges();
    }

    using (var session = theStore.QuerySession())
    {
        var metadata = session.MetadataFor(shop);

        metadata.ShouldNotBeNull();
        metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
        metadata.LastModified.ShouldNotBe(default);
        metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
        metadata.DocumentType.ShouldBeNull();
        metadata.Deleted.ShouldBeFalse();
        metadata.DeletedAt.ShouldBeNull();
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/fetching_entity_metadata.cs#L22-L50' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_resolving_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Correlation, Causation, and Last Modified By Tracking

- show `ITracked`
- show how to use `IDocumentSession`

## Opting out of all Metadata Tracking

- show doing this on a single document type
- show doing this globally

## Tracking "Soft-Deleted" Information

TODO

## Tracking Version Information

TODO

## Custom Metadata

TODO

## Querying by Metadata

TODO
