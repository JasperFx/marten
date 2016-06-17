<!--Title:Working with Marten's Metadata Columns-->

When Marten generates a table for document storage it now adds three _metadata_ columns
that further describe the document:

1. `mt_last_modified` - a timestamp of the last time the document was modified
1. `mt_dotnet_type` - The `FullName` property of the actual .Net type persisted. This is strictly for information and is not used by Marten itself.
1. `mt_version` - A sequential (as of 1.0-alpha) Guid designating the revision of the document. Marten uses
   this column in its optimistic concurrency checks
   
## Finding the Metadata for a Document

You can find the metadata values for a given document object with the following mechanism
on `IDocumentStore.Advanced`:

<[sample:resolving_metadata]>

