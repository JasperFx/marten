# Deleting Documents

## Delete by Id or by Document

You can register document deletions with an active `IDocumentSession` by either the document itself or just by the document id to avoid having to fetch
a document from the database just to turn around and delete it. Deletions are executed within the single database transaction when you call
`IDocumentSession.SaveChanges()`.

The usage is shown below:

<!-- snippet: sample_deletes -->
<a id='snippet-sample_deletes'></a>
```cs
public void delete_documents(IDocumentSession session)
{
    var user = new User();

    session.Delete(user);
    session.SaveChanges();

    // OR

    session.Delete(user.Id);
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L7-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deletes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Delete by Linq Queries

New for Marten v0.8 is the ability to delete any documents of a certain type meeting a Linq expression using the new `IDocumentSession.DeleteWhere<T>()` method:

<!-- snippet: sample_DeleteWhere -->
<a id='snippet-sample_deletewhere'></a>
```cs
theSession.DeleteWhere<Target>(x => x.Double == 578);

theSession.SaveChanges();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/delete_many_documents_by_query_Tests.cs#L28-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deletewhere' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple things to note:

1. The actual Sql command to delete documents by a query is not executed until `IDocumentSession.SaveChanges()` is called
1. The bulk delete command runs in the same batched sql command and transaction as any other document updates or deletes
   in the session
