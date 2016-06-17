<!--Title:Deleting Documents-->
<!--Url:deleting-->

## Delete by Id or by Document

You can register document deletions with an active `IDocumentSession` by either the document itself or just by the document id to avoid having to fetch
a document from the database just to turn around and delete it. Deletions are executed within the single database transaction when you call
`IDocumentSession.SaveChanges()`.

The usage is shown below:

<[sample:deletes]>


## Delete by Linq Queries

New for Marten v0.8 is the ability to delete any documents of a certain type meeting a Linq expression using the new `IDocumentSession.DeleteWhere<T>()` method:

<[sample:DeleteWhere]>

A couple things to note:

1. The actual Sql command to delete documents by a query is not executed until `IDocumentSession.SaveChanges()` is called
1. The bulk delete command runs in the same batched sql command and transaction as any other document updates or deletes
   in the session


