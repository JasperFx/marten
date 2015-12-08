<!--Title:Marten as Document Db-->
<!--Url:documents-->

Many of the Marten contributors were fans of the existing RavenDb database, but wanted a more robust underlying technology. To that end, Marten was largely conceived of as a way to efficiently use the proven Postgresql database engine as a document database and act as a near drop-in replacement 
for RavenDb in several existing applications. As such, you may clearly see some influence from RavenDb on the Marten services and API's.  

## Document Store

To use Marten as a document database, you first need a Postgresql schema that will store your documents. Once you have that, getting started
with Marten can be as simple as opening a `DocumentStore` to your new Postgresql schema:

<[sample:start_a_store]>

The code sample above sets up document storage against the Postgresql at the connection string you supplied. In the default configuration,
Marten will build new database tables and functions for each new document type as needed **if those database schema objects do not already exist**.

While the default "auto-create" database schema objects is fantastic for a development time experience, you may not want Marten to be building out new database schema objects at production time. You might also want to override the default JSON serialization, tweak the document storage for performance, or opt into Postgresql 9.5's fancy new "upsert" capability. For customization, you have a different syntax for bootstrapping a `DocumentStore`:

<[sample:start_a_complex_store]>

For more information on using the `IDocumentStore` and configuring document storage, see:

* <[linkto:documentation/documents/customizing]>
* <[linkto:documentation/documents/json]>
* <[linkto:documentation/documents/bulk_insert]>
* <[linkto:documentation/documents/versioning]>
* <[linkto:documentation/documents/diagnostics]>
* <[linkto:documentation/documents/text_search]>
* <[linkto:documentation/documents/cleaning]>
* <[linkto:documentation/documents/hierarchies]>


## Querying and Loading Documents

Now that we've got a document store, we can use that to create a new `IQuerySession` object just for querying or loading documents from the database:

<[sample:start_a_query_session]>

For more information on the query support within Marten, see:

* <[linkto:documentation/documents/linq]>
* <[linkto:documentation/documents/loading]>
* <[linkto:documentation/documents/sql]>
* <[linkto:documentation/documents/batched_queries]>
* <[linkto:documentation/documents/query_json]>
* <[linkto:documentation/documents/compiled_queries]>


## Persisting Documents

The main service for updating and persisting documents is the `IDocumentSession`. An `IDocumentSession` is created by your `IDocumentStore` from above
in one of three ways:

<[sample:opening_sessions]>

In all cases, `IDocumentSession` has the same query and loading functions as the read only `IQuerySession`.

For more information on persisting documents and the different underlying behavior of the three flavors of `IDocumentSession` above, see:

* <[linkto:documentation/documents/persisting]>
* <[linkto:documentation/documents/partial_updates]>
* <[linkto:documentation/documents/deleting]>


