# Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<!-- snippet: sample_query_by_nullable_types -->
<a id='snippet-sample_query_by_nullable_types'></a>
```cs
public async Task query_by_nullable_type_nulls(IDocumentSession session)
{
    // You can use Nullable<T>.HasValue in Linq queries
    await session.Query<Target>().Where(x => !x.NullableNumber.HasValue).ToListAsync();
    await session.Query<Target>().Where(x => x.NullableNumber.HasValue).ToListAsync();

    // You can always search by field is NULL
    await session.Query<Target>().Where(x => x.Inner == null).ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L189-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_nullable_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Inside a child collection predicate, `!= null` follows C# semantics: a `null` member compared with `!=` matches. See
[How Marten Translates Collection Predicates](/documents/querying/linq/child-collections#how-marten-translates-collection-predicates).

## Optional Filters {#optional-filters}

A captured variable is evaluated on the client and sent as a parameter, and so is a captured `Nullable<T>`, its
`HasValue` and its `Value`. That lets you write a filter that only applies when a value was given:

<!-- snippet: sample_optional_filter_with_a_captured_nullable -->
<a id='snippet-sample_optional_filter_with_a_captured_nullable'></a>
```cs
// When excluded is null, the filter is skipped
Guid? excluded = null;
var results = await theSession.Query<CapturedNullableDoc>()
    .Where(x => !excluded.HasValue || x.Id != excluded)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/captured_nullable_members_in_where.cs#L61-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_optional_filter_with_a_captured_nullable' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Static members, and indexing into a captured or static array or dictionary, are evaluated on the client the same way.
