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

<<< @/../src/Marten.Testing/Acceptance/computed_indexes.cs#sample_using-a-simple-calculated-index

In the configuration shown above, Marten generates a database index in Postgresql:

```sql
CREATE INDEX mt_doc_user_idx_user_name ON public.mt_doc_user ((data ->> 'UserName'));
```

You can also create calculated indexes for deep or nested properties like this:

<<< @/../src/Marten.Testing/Acceptance/computed_indexes.cs#sample_deep-calculated-index

The configuration above creates an index like this:

```sql
CREATE INDEX mt_doc_target_idx_inner_color ON public.mt_doc_target (((data -> 'Inner' ->> 'Color')::int));
```

Or create calculated multi-property indexes like this:

<<< @/../src/Marten.Testing/Examples/MultiPropertyCalculatedIndexExamples.cs#sample_multi-property-calculated-index

The configuration above creates an index like this:

```
CREATE INDEX mt_doc_user_idx_first_namelast_name ON public.mt_doc_user USING btree (((data ->> 'FirstName'::text)), ((data ->> 'LastName'::text)))
```

## Customizing a Calculated Index

You have some ability to customize the calculated index by passing a second Lambda `Action` into
the `Index()` method as shown below:

<<< @/../src/Marten.Testing/Acceptance/computed_indexes.cs#sample_customizing-calculated-index
