# Ignore Indexes

Any custom index on a Marten defined document table added outside of Marten can potentically cause issues with Marten schema migration detection and delta computation. Marten provides a mechanism to ignore those indexes using `IgnoreIndex(string indexName)`.

<!-- snippet: sample_IgnoreIndex -->
<a id='snippet-sample_ignoreindex'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.Schema.For<User>().IgnoreIndex("foo");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Configuration/ignoring_indexes_on_document_table.cs#L27-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ignoreindex' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
