<!--title: Duplicated Fields for Faster Querying-->

According to our testing, the single best thing you can do to speed up queries against the JSONB documents
is to duplicate a property or field within the JSONB structure as a separate database column on the document
table. When you issue a Linq query using this duplicated property or field, Marten is able to write the SQL
query to run against the duplicated field instead of using JSONB operators. This of course only helps for 
queries using the duplicated field.

To create a searchable field, you can use the `[DuplicateField]` attribute like this:

<[sample:using_attributes_on_document]>

By default, Marten adds a [btree index](http://www.postgresql.org/docs/9.4/static/indexes-types.html) (the Postgresql default) to a searchable index, but you can also 
customize the generated index with the syntax shown below:

<[sample:IndexExamples]>
