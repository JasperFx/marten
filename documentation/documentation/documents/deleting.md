<!--Title:Deleting Documents-->
<!--Url:deleting-->

You can register document deletions with an active `IDocumentSession` by either the document itself or just by the document id to avoid having to fetch
a document from the database just to turn around and delete it. Deletions are executed within the single database transaction when you call
`IDocumentSession.SaveChanges()`.

The usage is shown below:

<[sample:deletes]>
