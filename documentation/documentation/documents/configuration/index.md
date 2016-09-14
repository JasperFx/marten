<!--Title:Customizing Document Storage-->
<!--Url:customizing-->

While you can certainly write your own [DDL](https://en.wikipedia.org/wiki/Data_definition_language) 
and SQL queries for optimizing data fetching, Marten gives you a couple options for speeding up queries -- 
which all come at the cost of slower inserts because it's an imperfect world. Marten supports the ability to configure:

* Indexes on the JSONB data field itself
* Duplicate properties into separate database fields with a matching index for optimized querying
* Choose how Postgresql will search within JSONB documents
* DDL generation rules
* How documents will be deleted

My own personal bias is to avoid adding persistence concerns directly to the document types, but other developers
will prefer to use either attributes or the new embedded configuration option with the thinking that it's
better to keep the persistence configuration on the document type itself for easier traceability. Either way,
Marten has you covered with the various configuration options shown here.

The configuration options you'll most likely use are:

<[TableOfContents]>


## Custom StoreOptions

It's perfectly valid to create your own subclass of `StoreOptions` that configures itself, as shown below. 

<[sample:custom-store-options]>

This strategy might be beneficial if you need to share Marten configuration across different applications
or testing harnesses or custom migration tooling.


## MartenRegistry

While there are some limited abilities to configure storage with attributes, the most complete option right now 
is a fluent interface implemented by the `MartenRegistry`. To configure a Marten document store, first write
your own subclass of `MartenRegistry` and place declarations in the constructor function like this example:

<[sample:MyMartenRegistry]>

To apply your new `MartenRegistry`, just include it when you bootstrap the `IDocumentStore` as in this example:

<[sample:using_marten_registry_to_bootstrap_document_store]>

Do note that you could happily use multiple `MartenRegistry` classes in larger applications if that is advantageous.

If you dislike using infrastructure attributes in your application code, you will probably prefer to use MartenRegistry.


## Custom Attributes

If there's some kind of customization you'd like to use attributes for that isn't already supported by Marten, 
you're still in luck. If you write a subclass of the `MartenAttribute` shown below:

<[sample:MartenAttribute]>

And decorate either classes or individual field or properties on a document type, your custom attribute will be
picked up and used by Marten to configure the underlying `DocumentMapping` model for that document type. The
`MartenRegistry` is just a fluent interface over the top of this same `DocumentMapping` model.

As an example, an attribute to add a gin index to the JSONB storage for more efficient adhoc querying of a document
would look like this:

<[sample:GinIndexedAttribute]>


## Embedding Configuration in Document Types

Lastly, Marten can examine the document types themselves for a `public static ConfigureMarten()` method
and invoke that to let the document type make its own customizations for its storage. Here's an example from
the unit tests:

<[sample:ConfigureMarten-generic]>

The `DocumentMapping` type is the core configuration class representing how a document type is persisted or
queried from within a Marten application. All the other configuration options end up writing to a
`DocumentMapping` object.

You can optionally take in the more specific `DocumentMapping<T>` for your document type to get at 
some convenience methods for indexing or duplicating fields that depend on .Net Expression's:

<[sample:ConfigureMarten-specifically]>