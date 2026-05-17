# Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<!-- snippet: sample_boolean_queries -->
<a id='snippet-sample_boolean_queries'></a>
```cs
public async Task query_by_booleans(IDocumentSession session)
{
    // Flag is a boolean property.

    // Where Flag is true
    await session.Query<Target>().Where(x => x.Flag).ToListAsync();
    // or
    await session.Query<Target>().Where(x => x.Flag == true).ToListAsync();

    // Where Flag is false
    await session.Query<Target>().Where(x => !x.Flag).ToListAsync();
    // or
    await session.Query<Target>().Where(x => x.Flag == false).ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L158-L174' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_boolean_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
