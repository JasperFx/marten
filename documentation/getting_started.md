<!--Title:Getting Started-->
<!--Url:getting_started-->

First, go get the Marten library from Nuget:

<pre>
PM> Install-Package StructureMap
</pre>

Or, using paket:

<pre>
paket add nuget Marten
</pre>

The next step is to get access to a Postgresql 9.4+ database schema. If you want to let Marten build database schema objects on the fly at development time, 
make sure that your user account has rights to execute `CREATE TABLE/FUNCTION` statements. 

Marten uses the [Npgsql](http://www.npgsql.org) library to access Postgresql from .Net, so you'll likely want to read their [documentation on connection string syntax](http://www.npgsql.org/doc/connection-string-parameters.html).

## Bootstrapping a Document Store

To start up Marten in a running application, you need to create a single `IDocumentStore` object. The quickest way is to start with 
all the default behavior and a connection string:

<[sample:start_a_store]>

Now, for your first document type, let's represent the users in our system:

<[sample:user_document]>

_For more information on document id's, see <[linkto:documentation/documents/document_ids]>._

And now that we've got a Postgresql schema and an `IDocumentStore`, let's start persisting and loading user documents:

<[sample:opening_sessions]>


## Integrating Marten with IoC Containers

The Marten team has striven to make the library perfectly usable without the usage of an IoC container, but you may still want to
use an IoC container specifically to manage dependencies and the lifecyle of Marten objects.

Using [StructureMap](http://structuremap.github.io) as the example container, we recommend registering Marten something like this:

<[sample:MartenServices]>

There are really only two key points here:

1. There should only be one `IDocumentStore` object instance created in your application, so I scoped it as a "Singleton" in the StructureMap container
1. The `IDocumentSession` service that you use to read and write documents should be scoped as "one per transaction." In typical usage, this 
   ends up meaning that an `IDocumentSession` should be scoped to a single HTTP request in web applications or a single message being handled in service
   bus applications.


There's a lot more capabilities than what we're showing here, so head on over to <[linkto:documentation/documentation]> to see what else Marten offers.