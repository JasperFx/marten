# Duplicated Fields for Faster Querying

According to our testing, the single best thing you can do to speed up queries against the JSONB documents
is to duplicate a property or field within the JSONB structure as a separate database column on the document
table. When you issue a Linq query using this duplicated property or field, Marten is able to write the SQL
query to run against the duplicated field instead of using JSONB operators. This of course only helps for
queries using the duplicated field.

To create a duplicated field, you can use the `[DuplicateField]` attribute like this:

<!-- snippet: sample_using_attributes_on_document -->
<a id='snippet-sample_using_attributes_on_document'></a>
```cs
[PropertySearching(PropertySearching.ContainmentOperator)]
public class Employee
{
    public int Id;

    // You can optionally override the Postgresql
    // type for the duplicated column in the document
    // storage table
    [DuplicateField(PgType = "text")]
    public string Category;

    // Defining a duplicate column with not null constraint
    [DuplicateField(PgType = "text", NotNull = true)]
    public string Department;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L27-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_attributes_on_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface off of `StoreOptions`:

<!-- snippet: sample_IndexExamples -->
<a id='snippet-sample_indexexamples'></a>
```cs
var store = DocumentStore.For(options =>
{
    // Add a gin index to the User document type
    options.Schema.For<User>().GinIndexJsonData();

    // Adds a basic btree index to the duplicated
    // field for this property that also overrides
    // the Postgresql database type for the column
    options.Schema.For<User>().Duplicate(x => x.FirstName, pgType: "varchar(50)");

    // Defining a duplicate column with not null constraint
    options.Schema.For<User>().Duplicate(x => x.Department, pgType: "varchar(50)", notNull: true);

    // Customize the index on the duplicated field
    // for FirstName
    options.Schema.For<User>().Duplicate(x => x.FirstName, configure: idx =>
    {
        idx.Name = "idx_special";
        idx.Method = IndexMethod.hash;
    });

    // Customize the index on the duplicated field
    // for UserName to be unique
    options.Schema.For<User>().Duplicate(x => x.UserName, configure: idx =>
    {
        idx.IsUnique = true;
    });

    // Customize the index on the duplicated field
    // for LastName to be in descending order
    options.Schema.For<User>().Duplicate(x => x.LastName, configure: idx =>
    {
        idx.SortOrder = SortOrder.Desc;
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L51-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_indexexamples' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the case above, Marten would add an extra columns to the generated `mt_doc_user` table with `first_name` and `department`. Some users find duplicated fields to be useful for user supplied SQL queries.

## Defining Not Null constraint

By default, the duplicate column is created with NULL constraint. If you want to define the duplicate column with a NOT NULL constraint, use `NotNull` property via `DuplicateFieldAttribute` or pass `notNull: true` for the `Duplicate` fluent interface. See the examples above.

## Indexing

By default, Marten adds a [btree index](http://www.postgresql.org/docs/9.4/static/indexes-types.html) (the Postgresql default) to a searchable index, but you can also
customize the generated index with the syntax shown above: The second [nested closure](http://martinfowler.com/dslCatalog/nestedClosure.html) argument is an optional
mechanism to customize the database index generated for the duplicated field.
