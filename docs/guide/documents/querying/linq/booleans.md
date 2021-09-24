# Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<!-- snippet: sample_boolean_queries -->
<a id='snippet-sample_boolean_queries'></a>
```cs
public void query_by_booleans(IDocumentSession session)
{
    // Flag is a boolean property.

    // Where Flag is true
    session.Query<Target>().Where(x => x.Flag).ToArray();
    // or
    session.Query<Target>().Where(x => x.Flag == true).ToArray();

    // Where Flag is false
    session.Query<Target>().Where(x => !x.Flag).ToArray();
    // or
    session.Query<Target>().Where(x => x.Flag == false).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L128-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_boolean_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
