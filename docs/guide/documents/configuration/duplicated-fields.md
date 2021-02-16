# Duplicated Fields for Faster Querying

According to our testing, the single best thing you can do to speed up queries against the JSONB documents
is to duplicate a property or field within the JSONB structure as a separate database column on the document
table. When you issue a Linq query using this duplicated property or field, Marten is able to write the SQL
query to run against the duplicated field instead of using JSONB operators. This of course only helps for
queries using the duplicated field.

To create a duplicated field, you can use the `[DuplicateField]` attribute like this:

<<< @/../src/Marten.Testing/Examples/MartenRegistryExamples.cs#sample_using_attributes_on_document

Or by using the fluent interface off of `StoreOptions`:

<<< @/../src/Marten.Testing/Examples/MartenRegistryExamples.cs#sample_IndexExamples

In the case above, Marten would add an extra columns to the generated `mt_doc_user` table with `first_name` and `department`. Some users find duplicated fields to be useful for user supplied SQL queries.

## Defining Not Null constraint

By default, the duplicate column is created with NULL constraint. If you want to define the duplicate column with a NOT NULL constraint, use `NotNull` property via `DuplicateFieldAttribute` or pass `notNull: true` for the `Duplicate` fluent interface. See the examples above.

## Indexing

By default, Marten adds a [btree index](http://www.postgresql.org/docs/9.4/static/indexes-types.html) (the Postgresql default) to a searchable index, but you can also
customize the generated index with the syntax shown above: The second [nested closure](http://martinfowler.com/dslCatalog/nestedClosure.html) argument is an optional
mechanism to customize the database index generated for the duplicated field.
