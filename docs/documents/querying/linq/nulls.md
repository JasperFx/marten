# Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<!-- snippet: sample_query_by_nullable_types -->
<a id='snippet-sample_query_by_nullable_types'></a>
```cs
public void query_by_nullable_type_nulls(IDocumentSession session)
{
    // You can use Nullable<T>.HasValue in Linq queries
    session.Query<Target>().Where(x => !x.NullableNumber.HasValue).ToArray();
    session.Query<Target>().Where(x => x.NullableNumber.HasValue).ToArray();

    // You can always search by field is NULL
    session.Query<Target>().Where(x => x.Inner == null);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L176-L187' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_nullable_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
