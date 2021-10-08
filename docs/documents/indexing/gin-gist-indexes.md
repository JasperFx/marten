# GIN or GiST Indexes

See [Exploring the Postgres GIN index](https://hashrocket.com/blog/posts/exploring-postgres-gin-index) for more information on the GIN index strategy within Postgresqsl.

To optimize a wider range of ad-hoc queries against the document JSONB, you can apply a [GIN index](http://www.postgresql.org/docs/9.4/static/gin.html) to
the JSON field in the database:

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

**Marten may be changed to make the GIN index on the data field be automatic in the future.**
