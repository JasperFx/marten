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

<<< @/../src/Marten.Testing/Acceptance/fetching_entity_metadata.cs#sample_resolving_metadata

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
