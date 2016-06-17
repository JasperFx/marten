<!--Title:Customizing Document Storage-->
<!--Url:customizing-->

While you can certainly write your own [DDL](https://en.wikipedia.org/wiki/Data_definition_language) 
and SQL queries for optimizing data fetching, Marten gives you a couple options for speeding up queries -- 
which all come at the cost of slower inserts because it's an imperfect world. Marten supports the ability to configure:

* Indexes on the JSONB data field itself
* Duplicate properties into separate database fields with a matching index for optimized querying
* Choose how Postgresql will search within JSONB documents

The configuration options you'll most likely use are:

<[TableOfContents]>


TODO(talk about writing a custom StoreOptions class to make it easier to reuse configuration)

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