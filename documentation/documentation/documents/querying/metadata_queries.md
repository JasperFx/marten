<!--Title:Metadata Queries-->

All documents stored by Marten have <[linkto:documentation/schema/metadata;title=Metadata]> attributes.
These could of course be queried using <[linkto:documentation/documents/querying/sql;title=PostgreSQL]> but there are some Linq
methods predefined.

You could also define your own, see <[linkto:documentation/documents/advanced/customizing_linq;title=Extending Marten's Linq Support]>

## Last Modified

Queries utilising `mt_last_modified`
* `ModifiedSince(DateTimeOffset)` - Return only documents modified since specific date (not inclusive)
* `ModifiedBefore(DateTimeOffset)` - Return only documents modified before specific date (not inclusive)

## Deleted

See <[linkto:documentation/documents/advanced/soft_deletes;title=Soft Deletes]> for further details

Queries utilising `mt_deleted`
* `IsDeleted()` - Return only deleted documents
* `MaybeDeleted()` - Return all documents whether deleted or not

Queries utilising `mt_deleted_at`
* `DeletedSince(DateTimeOffset)` - Return only documents deleted since specific date (not inclusive)
* `DeletedBefore(DateTimeOffset)` - Return only documents deleted before specific date (not inclusive)

## Indexing

See <[linkto:documentation/documents/configuration/metadata_index]> for information on how to enable predefined
indexing
