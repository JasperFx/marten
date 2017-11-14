<!--title:Enlisting in Existing Transactions-->

Before Marten 2.4.0, a Marten `IDocumentSession` always controlled the lifecycle of its underlying database
connection and transaction boundaries. With the 2.4.0+ release, you can pass in an existing transaction or connection, direct
Marten to enlist in an ambient transaction scope, and even direct Marten on whether or not it owns the transaction boundaries
to override whether or not `SaveChanges/SaveChangesAsync` will commit the underlying transaction.

Do note that the transaction scope enlisting is only available in either the full .Net framework (> .Net 4.6) or applications targeting 
Netstandard 2.0.

<[sample:passing-in-existing-connections-and-transactions]>

