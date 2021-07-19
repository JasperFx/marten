# Marten as Document DB

Many of the Marten contributors were fans of the existing RavenDb database, but wanted a more robust underlying technology. To that end, Marten was largely conceived of as a way to efficiently use the proven Postgresql database engine as a document database and act as a near drop-in replacement
for RavenDb in several existing applications. As such, you may clearly see some influence from RavenDb on the Marten services and API's.

## Document Store

To use Marten as a document database, you first need a Postgresql schema that will store your documents. Once you have that, getting started
with Marten can be as simple as opening a `DocumentStore` to your new Postgresql schema:

<!-- snippet: sample_start_a_store -->
<a id='snippet-sample_start_a_store'></a>
```cs
var store = DocumentStore
    .For("host=localhost;database=marten_testing;password=mypassword;username=someuser");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L34-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The code sample above sets up document storage against the Postgresql at the connection string you supplied. In this "quickstart" configuration,
Marten will build new database tables and functions for each new document type as needed **if those database schema objects do not already exist**.

::: tip INFO
As of Marten v3.0, document storage tables, by default, are created and updated, but never dropped (destroyed). To let Marten perform any destructive operations, such as adding a searchable field or changing the document id type, change the store configuration to `StoreOptions.AutoCreateSchemaObjects = AutoCreate.All`.
:::

While the default "auto-create or update" (`AutoCreate.CreateOrUpdate`) database schema management is fantastic for a development time experience, you may not want Marten to be building out new database schema objects at production time. You might also want to override the default JSON serialization, tweak the document storage for performance, or opt into Postgresql 9.5's fancy new "upsert" capability. For customization, you have a different syntax for bootstrapping a `DocumentStore`:

<!-- snippet: sample_start_a_complex_store -->
<a id='snippet-sample_start_a_complex_store'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Turn this off in production
    _.AutoCreateSchemaObjects = AutoCreate.None;

    // This is still mandatory
    _.Connection("some connection string");

    // Override the JSON Serialization
    _.Serializer<TestsSerializer>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L79-L91' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_complex_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For more information on using the `IDocumentStore` and configuring document storage, see:

* [Configuration](/guide/documents/configuration/)
* [JSON serialization](/guide/documents/json/)
* [Bulk insert](/guide/documents/basics/bulk-insert)
* [Initial data](/guide/documents/basics/initial-data)
* [Optimistic concurrency](/guide/documents/advanced/optimistic-concurrency)
* [Diagnostics and instrumentation](/guide/documents/diagnostics)
* [Tearing down document storage](/guide/documents/advanced/cleaning)
* [Document hierarchies](/guide/documents/advanced/hierarchies)

## Querying and Loading Documents

Now that we've got a document store, we can use that to create a new `IQuerySession` object just for querying or loading documents from the database:

<!-- snippet: sample_start_a_query_session -->
<a id='snippet-sample_start_a_query_session'></a>
```cs
using (var session = store.QuerySession())
{
    var internalUsers = session
        .Query<User>().Where(x => x.Internal).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L39-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_query_session' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For more information on the query support within Marten, check [document querying](/guide/documents/querying/)

## Persisting Documents

The main service for updating and persisting documents is the `IDocumentSession`. An `IDocumentSession` is created by your `IDocumentStore` from above
in one of three ways:

<!-- snippet: sample_opening_sessions -->
<a id='snippet-sample_opening_sessions'></a>
```cs
// Open a session for querying, loading, and
// updating documents
using (var session = store.LightweightSession())
{
    var user = new User { FirstName = "Han", LastName = "Solo" };
    session.Store(user);

    session.SaveChanges();
}

// Open a session for querying, loading, and
// updating documents with a backing "Identity Map"
using (var session = store.OpenSession())
{
    var existing = session
        .Query<User>()
        .Where(x => x.FirstName == "Han" && x.LastName == "Solo")
        .Single();
}

// Open a session for querying, loading, and
// updating documents that performs automated
// "dirty" checking of previously loaded documents
using (var session = store.DirtyTrackedSession())
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L47-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_opening_sessions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In all cases, `IDocumentSession` has the same query and loading functions as the read only `IQuerySession`.

For more information on persisting documents and the different underlying behavior of the three flavors of `IDocumentSession` above, see:

* [Storing documents and unit of work](/guide/documents/basics/persisting)
* [Patching API](/guide/documents/advanced/patch-api)
* [Deleting documents](/guide/documents/basics/deleting)
