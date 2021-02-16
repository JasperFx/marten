# Metadata Queries

All documents stored by Marten have [metadata](/guide/schema/metadata) attributes.
These could of course be queried using [Postgres SQL](/guide/documents/querying/sql) but there are some Linq
methods predefined.

You could also define your own, see extending [Marten's Linq support](/guide/documents/advanced/customizing-linq)

## Last Modified

Queries utilising `mt_last_modified`

* `ModifiedSince(DateTimeOffset)` - Return only documents modified since specific date (not inclusive)
* `ModifiedBefore(DateTimeOffset)` - Return only documents modified before specific date (not inclusive)

## Deleted

See [soft deletes](/guide/documents/advanced/soft-deletes) for further details

Queries utilising `mt_deleted`

* `IsDeleted()` - Return only deleted documents
* `MaybeDeleted()` - Return all documents whether deleted or not

Queries utilising `mt_deleted_at`

* `DeletedSince(DateTimeOffset)` - Return only documents deleted since specific date (not inclusive)
* `DeletedBefore(DateTimeOffset)` - Return only documents deleted before specific date (not inclusive)

## Indexing

See [metadata index](/guide/documents/configuration/metadata-indexes) for information on how to enable predefined
indexing
