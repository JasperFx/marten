# Calculated Index

::: tip INFO
Calculated indexes are a great way to optimize the querying of a document type without incurring
potentially expensive schema changes and extra runtime insert costs.
:::

::: warning
At this time, calculated indexes do not work against `DateTime` or `DateTimeOffset` fields. You will have
to resort to a duplicated field for these types.
:::

If you want to optimize a document type for searches on a certain field within the JSON body without incurring the potential cost of the duplicated field, you can take advantage of Postgresql's [computed index feature](https://www.postgresql.org/docs/9.5/static/indexes-expressional.html) within Marten with this syntax:

<!-- snippet: sample_using-a-simple-calculated-index -->
<a id='snippet-sample_using-a-simple-calculated-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.DatabaseSchemaName = "examples";

    // This creates
    _.Schema.For<User>().Index(x => x.UserName);
});

using (var session = store.QuerySession())
{
    // Postgresql will be able to use the computed
    // index generated from above
    var somebody = session
        .Query<User>()
        .FirstOrDefault(x => x.UserName == "somebody");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/computed_indexes.cs#L21-L40' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-a-simple-calculated-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the configuration shown above, Marten generates a database index in Postgresql:

```sql
CREATE INDEX mt_doc_user_idx_user_name ON public.mt_doc_user ((data ->> 'UserName'));
```

You can also create calculated indexes for deep or nested properties like this:

<!-- snippet: sample_deep-calculated-index -->
<a id='snippet-sample_deep-calculated-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.Schema.For<Target>().Index(x => x.Inner.Color);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/computed_indexes.cs#L67-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep-calculated-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The configuration above creates an index like this:

```sql
CREATE INDEX mt_doc_target_idx_inner_color ON public.mt_doc_target (((data -> 'Inner' ->> 'Color')::int));
```

Or create calculated multi-property indexes like this:

<!-- snippet: sample_multi-property-calculated-index -->
<a id='snippet-sample_multi-property-calculated-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    var columns = new Expression<Func<User, object>>[]
    {
        x => x.FirstName,
        x => x.LastName
    };
    _.Schema.For<User>().Index(columns);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MultiPropertyCalculatedIndexExamples.cs#L11-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multi-property-calculated-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The configuration above creates an index like this:

```
CREATE INDEX mt_doc_user_idx_first_namelast_name ON public.mt_doc_user USING btree (((data ->> 'FirstName'::text)), ((data ->> 'LastName'::text)))
```

## Customizing a Calculated Index

You have some ability to customize the calculated index by passing a second Lambda `Action` into
the `Index()` method as shown below:

<!-- snippet: sample_customizing-calculated-index -->
<a id='snippet-sample_customizing-calculated-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // The second, optional argument to Index()
    // allows you to customize the calculated index
    _.Schema.For<Target>().Index(x => x.Number, x =>
            {
                // Change the index method to "brin"
                x.Method = IndexMethod.brin;

                // Force the index to be generated with casing rules
                x.Casing = ComputedIndex.Casings.Lower;

                // Override the index name if you want
                x.Name = "mt_my_name";

                // Toggle whether or not the index is concurrent
                // Default is false
                x.IsConcurrent = true;

                // Toggle whether or not the index is a UNIQUE
                // index
                x.IsUnique = true;

                // Toggle whether index value will be constrained unique in scope of whole document table (Global)
                // or in a scope of a single tenant (PerTenant)
                // Default is Global
                x.TenancyScope = Schema.Indexing.Unique.TenancyScope.PerTenant;

                // Partial index by supplying a condition
                x.Predicate = "(data ->> 'Number')::int > 10";
            });

    // For B-tree indexes, it's also possible to change
    // the sort order from the default of "ascending"
    _.Schema.For<User>().Index(x => x.LastName, x =>
            {
                // Change the index method to "brin"
                x.SortOrder = SortOrder.Desc;
            });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/computed_indexes.cs#L80-L123' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customizing-calculated-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
