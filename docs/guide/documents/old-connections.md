# Connection Handling in Marten

Marten uses a single connection to the Postgresql database in each `IQuerySession` or `IDocumentSession`.
The connection is only opened on the first call to the database, but after that remains open until the `IQuerySession`/`IDocumentSession` is disposed. A couple things to note:

* It's imperative that any `IQuerySession`/`IDocumentSession` opened is disposed in order to recover and reuse
  connections to the underlying database
* Because the connection is "sticky" to the session, you can utilize serializable transactions. In the future, Marten will
  also enable you to opt into [locking documents read from the session](https://github.com/JasperFx/marten/issues/356).

There is no place within Marten where it keeps a stateful connection open across sessions.
