# Flat Table Projections

Marten has yet another projection recipe for writing event data to flat projections. 

Let’s dive right into a sample usage of this. If you’re a software developer long enough and move around just a little bit, 
you’re going to get sucked into building a workflow for importing flat files of dubious quality from external partners or 
customers. I’m going to claim that event sourcing is a good fit for this problem domain for event sourcing (and 
also suggesting this pretty strongly at work). That being said, here’s what the event types might look like that are 
recording the progress of a file import:

snippet: sample_flat_table_events

At some point, we’re going to want to apply some metrics to the execution history to understand the average size of the 
incoming files, what times of the day have more or less traffic, and performance information broken down by file size, 
file type, and who knows what. This sounds to me like a perfect use case for SQL queries against a flat table.

Enter Marten flat table projection functionality. First off, let’s do this simply by writing some explicit SQL in a 
new projection that we can replay against the existing events when we’re ready. I’m going to use Marten’s 
`EventProjection` as a base class in this case:

snippet: sample_import_sql_projection

A couple notes about the code above:

We’ve invested a huge amount of time in Marten and the related Weasel library building in robust schema management. 
The `Table` model I’m using up above comes from Weasel, and this allows a Marten application using this projection 
to manage the table creation in the underlying database for us. This new table would be part of all Marten’s built in 
schema management functionality.

The `QueueSqlCommand()` functionality gives you the ability to add raw SQL commands to be executed as part of a 
Marten unit of work transaction. It’s important to note that the QueueSqlCommand() method doesn’t execute inline, 
rather it adds the SQL you enqueue to be executed in a batch query when you eventually call the holding 
`IDocumentSession.SaveChangesAsync()`. I can’t stress this enough, it has consistently been a big performance gain in 
Marten to batch up queries to the database server and reduce the number of network round trips.

The `Project()` methods are a naming convention with Marten’s EventProjection. The first argument is always 
assumed to be the event type. In this case though, it’s legal to use Marten’s `IEvent<T>` envelope type to 
allow you access to event metadata like timestamps, version information, and the containing stream identity.

Now, let’s use Marten’s brand `FlatTableProjection` recipe to do a little more advanced version of the earlier projection:

snippet: sample_flat_import_projection

A couple notes on this version of the code:

* `FlatFileProjection` is adding columns to its table based on the designated column mappings. 
  You can happily customize the `FlatFileProjection.Table` object to add indexes, constraints, or defaults.
* Marten is able to apply schema migrations and manage the table from the `FlatFileProjection` as long as it’s registered with Marten
* When you call `Map(x => x.ActivityType)`, Marten is by default mapping that to a kebab-cased derivation of the member 
  name for the column, so “activity_type”. You can explicitly map the column name yourself.
* The call to `Map(expression)` chains a fluent builder for the table column if you want to further customize the table 
  column with default values or constraints like the `NotNull()`
* In this case, I’m building a database row per event stream. The `FlatTableProjection` can also map to arbitrary 
  members of each event type
* The `Project<T>(lambda)` configuration leads to a runtime, code generation of a Postgresql upsert command so 
  as to not be completely dependent upon events being captured in the exact right order. I think this will be more 
  robust in real life usage than the first, more explicit version.

The `FlatTableProjection` in its first incarnation is not yet able to use event metadata.
