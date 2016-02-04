<!--Title: Request Counting and Throttling-->

For the purpose of ensuring that your system under development is not being too "chatty" to the database, Marten provides an "opt in" feature
called the "Request Counter" that can be used to either record or assert failure conditions if a single `IQuerySession` or `IDocumentSession` passes a configured number of database requests.

<div class="alert alert-warning" role="alert">The Marten team strongly recommends that you do not use the request counter with thrown exceptions in production. This functionality has been a major source of aggravation with other document databases.</div>

For the first example, let's just say that we want to get some output to the debug output if a session has more than 25 calls to the underlying database:

<[sample:request-counter-trips-off-debug-message]>

For another example, if at development time you want to throw an exception if a session has more than 25 calls to the underlying database:

<[sample:request-counter-throws-exception]>
