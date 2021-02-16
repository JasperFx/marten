# Javascript Transformations

The [patch API](/guide/documents/advanced/patch-api) provides some out of the box recipes for common document transforms and the [document projections](/guide/documents/querying/projections) in the Linq support gives you the ability to do some basic transformations of the persisted JSON data in the database as part of querying. If your needs fall outside of these simple built in mechanism, you're still in luck because you can resort to using custom Javascript functions that will run inside of Postgresql itself to do more advanced document transformations.

At this point, Marten supports these use cases:

1. Transforming the data in one or more documents to apply some kind of structural migration to persisted documents, like you would need to do if
   the application code no longer matches the JSON previously stored
1. Creating a "readside" view of a persisted document as part of a Linq query.
1. Transform the raw document data to a completely different .Net type as part of a Linq query

## Creating and Loading a Javascript Function

Javascript transformations work in Marten by first allowing you to write your Javascript function into a single file like this one:

<<< @/../src/Marten.Testing/get_fullname.js#sample_get_fullname

You'll notice a couple things:

* Marten requires you to export the transformation function with the `module.exports =` syntax familiar from [CommonJS](http://wiki.commonjs.org/wiki/CommonJS) or Node.js development.
  ES6 modules are not supported at this time.
* Marten expects the transformation function to take in a single argument for the current JSON data and return the new JSON data

::: tip INFO
There is some thought and even infrastructure for doing Javascript transformations with multiple, related documents, but that feature will not likely make it into Marten 1.0.
:::

To load a Javascript function into your Marten-ized Postgresql database, use this syntax as part of bootstrapping
your Marten `IDocumentStore`:

<<< @/../src/Marten.Testing/Acceptance/document_transforms.cs#sample_loading_js_transform_files

Behind the scenes, Marten creates a `TransformFunction` object in the document store that knows how governs the construction and update
of a [PLV8 function](http://pgxn.org/dist/plv8/doc/plv8.html) that will wrap your raw Javascript function to expose it to Postgresql:

```sql
CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc jsonb)
  RETURNS jsonb AS
$BODY$

  var module = {export: {}};

module.exports = function (doc) {
    return {fullname: doc.FirstName + ' ' + doc.LastName};
}

  var func = module.exports;

  return func(doc);

$BODY$
  LANGUAGE plv8 IMMUTABLE STRICT
  COST 100;
```

The Javascript functions are managed roughly the same way as all other schema objects. If you are running your `IDocumentStore` with
the `StoreOptions.AutoCreateSchemaObjects` option set to anything but `None`, Marten will attempt to automatically update
your database schema with the current version of the Javascript wrapper function. It does this on only the first usage of the
named transform, and works by just doing a `string.Contains()` check against the existing function in the database schema.

In the case of `StoreOptions.AutoCreateSchemaObjects = None`, the Javascript transform functions are evaluated and output through
the [schema migrations](/guide/schema/migrations).

## Using a Javascript Transform in Linq Queries

Once you have a Javascript transform loaded into the `IDocumentStore`, you can do live transformations inside
of Linq queries. If you only care about the transformed JSON, you use this syntax:

<<< @/../src/Marten.Testing/Acceptance/select_with_transformation.cs#sample_using_transform_to_json

If you want to retrieve the results deserialized to another type, you can use the `TransformTo<T>(transformName)`
method shown below:

<<< @/../src/Marten.Testing/Acceptance/select_with_transformation.cs#sample_transform_to_another_type

You can also use `TransformToJson()` inside of a [compiled query](/guide/documents/querying/compiled-queries):

<<< @/../src/Marten.Testing/Acceptance/select_with_transformation.cs#sample_transform_to_json_in_compiled_query

## Document Transformations

The persisted JSON documents in Marten are a reflection of your .Net classes. Great, that makes it absurdly easy to keep the database schema
in synch with your application code at development time -- especially compared to the typical development process against a relational database.
However, what happens when you really do need to make breaking changes or additions to a document type but you already have loads of
persisted documents in your Marten database with the old structure?

To that end, Marten allows you to use Javascript functions to alter the existing documents in the database. As an example,
let's go back to the User document type and assume for some crazy reason that we didn't immediately issue a user name to some subset of users.
As a default, we might just assign their user names by combining their first and last names like so:

<<< @/../src/Marten.Testing/default_username.js#sample_default_username

To apply this transformation to existing rows in the database, Marten exposes this syntax:

<<< @/../src/Marten.Testing/Acceptance/document_transforms.cs#sample_transform_example
