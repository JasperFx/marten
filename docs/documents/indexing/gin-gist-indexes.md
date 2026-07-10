# GIN or GiST Indexes

See [Exploring the Postgres GIN index](https://hashrocket.com/blog/posts/exploring-postgres-gin-index) for more information on the GIN index strategy within Postgresql.

To optimize a wider range of ad-hoc queries against the document JSONB, you can apply a [GIN index](http://www.postgresql.org/docs/9.4/static/gin.html) to
the JSON field in the database:

<!-- snippet: sample_indexexamples -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L66-L102' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_indexexamples' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Marten may be changed to make the GIN index on the data field be automatic in the future.**

## GIN Indexes for Child Collections <Badge type="tip" text="9.15" />

Containment filters against a child collection — what Marten generates for
`Where(x => x.Children.Any(c => c.Name == "x"))` — compare against the expression
`data -> 'Children'`, which the whole-document index from `GinIndexJsonData()` cannot serve.
Use `GinIndexJsonDataMember()` to create an expression GIN index scoped to one member:

```cs
var store = DocumentStore.For(options =>
{
    // CREATE INDEX ... ON mt_doc_order USING gin ((data -> 'Lines') jsonb_path_ops)
    options.Schema.For<Order>().GinIndexJsonDataMember(x => x.Lines);

    // Nested member paths follow the JSON structure
    options.Schema.For<Order>().GinIndexJsonDataMember(x => x.Customer.Addresses);
});
```

The member-scoped index is substantially smaller than the whole-document index and accelerates
the containment (`@>`) strategies described in
[querying within child collections](/documents/querying/linq/child-collections#how-marten-translates-collection-predicates-),
including equality `Any()` predicates, OR-of-equality predicates, and the equality pre-filter of
mixed predicates. Whole-element `Contains()` queries anchor at the document root and are served
by the plain `GinIndexJsonData()` index instead.
