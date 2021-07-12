# Enlisting in Existing Transactions

Before Marten 2.4.0, a Marten `IDocumentSession` always controlled the lifecycle of its underlying database
connection and transaction boundaries. With the 2.4.0+ release, you can pass in an existing transaction or connection, direct
Marten to enlist in an ambient transaction scope, and even direct Marten on whether or not it owns the transaction boundaries
to override whether or not `SaveChanges/SaveChangesAsync` will commit the underlying transaction.

Do note that the transaction scope enlisting is only available in either the full .Net framework (> .Net 4.6) or applications targeting
Netstandard 2.0.

<!-- snippet: sample_passing-in-existing-connections-and-transactions -->
<a id='snippet-sample_passing-in-existing-connections-and-transactions'></a>
```cs
public void samples(IDocumentStore store, NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    // Use an existing connection, but Marten still controls the transaction lifecycle
    var session1 = store.OpenSession(new SessionOptions
    {
        Connection = connection
    });

    // Enlist in an existing Npgsql transaction, but
    // choose not to allow the session to own the transaction
    // boundaries
    var session2 = store.OpenSession(new SessionOptions
    {
        Transaction = transaction,
        OwnsTransactionLifecycle = false
    });

    // This is syntactical sugar for the sample above
    var session3 = store.OpenSession(SessionOptions.ForTransaction(transaction));

    // Enlist in the current, ambient transaction scope
    using (var scope = new TransactionScope())
    {
        var session4 = store.OpenSession(SessionOptions.ForCurrentTransaction());
    }

    // or this is the long hand way of doing the options above
    using (var scope = new TransactionScope())
    {
        var session5 = store.OpenSession(new SessionOptions
        {
            EnlistInAmbientTransactionScope = true,
            OwnsTransactionLifecycle = false
        });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/ability_to_use_an_existing_connection_and_transaction.cs#L27-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_passing-in-existing-connections-and-transactions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
