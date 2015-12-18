<!--Title:Customizing Document Storage and Querying-->
<!--Url:customizing-->

While you can certainly write your own [DDL](https://en.wikipedia.org/wiki/Data_definition_language) 
and SQL queries for optimizing data fetching, Marten gives you a couple options for speeding up queries -- 
which all come at the cost of slower inserts because it's an imperfect world. Marten supports the ability to configure:

* Indexes on the JSONB data field itself
* Opt into a more efficient "upsert" style for Postgresql 9.5
* Duplicate properties into separate database fields with a matching index for optimized querying
* Choose how Postgresql will search within JSONB documents

At some point in the future Marten may support document hierarchies and more table customizations (foreign keys? table names?).



## MartenRegistry

While there are some limited abilities to configure storage with attributes, the most complete option right now 
is a fluent interface implemented by the `MartenRegistry`. To configure a Marten document store, first write
your own subclass of `MartenRegistry` and place declarations in the constructor function like this example:

<[sample:MyMartenRegistry]>

To apply your new `MartenRegistry`, just include it when you bootstrap the `IDocumentStore` as in this example:

<[sample:using_marten_registry_to_bootstrap_document_store]>

Do note that you could happily use multiple `MartenRegistry` classes in larger applications if that is advantageous.

If you dislike using infrastructure attributes in your application code, you will probably prefer to use MartenRegistry.



## Searchable Fields

According to our testing, the single best thing you can do to speed up queries against the JSONB documents
is to duplicate a property or field within the JSONB structure as a separate database column on the document
table. When you issue a Linq query using this duplicated property or field, Marten is able to write the SQL
query to run against the duplicated field instead of using JSONB operators. This of course only helps for 
queries using the duplicated field.

To create a searchable field, you can use the `[Searchable]` attribute like this:

<[sample:using_attributes_on_document]>

By default, Marten adds a [btree index](http://www.postgresql.org/docs/9.4/static/indexes-types.html) (the Postgresql default) to a searchable index, but you can also 
customize the generated index with the syntax shown below:

<[sample:IndexExamples]>


## Gin Indexes

To optimize a wider range of adhoc queries against the document JSONB, you can apply a [Gin index](http://www.postgresql.org/docs/9.4/static/gin.html) to
the JSON field in the database:

<[sample:IndexExamples]>

**Marten may be changed to make the Gin index on the data field be automatic in the future.**



## "UpsertStyle"

Marten started with the assumption that we would target Postgresql 9.5 and its [new "upsert" functionality](https://wiki.postgresql.org/wiki/What's_new_in_PostgreSQL_9.5),
but we have since backed off to making the older Postgresql upsert style the default with an "opt-in" choice
to use the 9.5 syntax:

<[sample:setting_upsert_style]>



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