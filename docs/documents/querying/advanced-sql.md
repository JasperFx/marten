# Advanced querying with Postgresql SQL

Besides Linq queries or simple raw SQL queries via `session.Query<T>("where...")`, it is also possible to do even more complex SQL queries via `session.AdvancedSqlQueryAsync<T>()`.
With this method Marten does not try to add any missing parts to the SQL query, instead you have to provide the whole query string yourself.

Marten just makes some assumptions on how the schema of the SQl query result must look like, in order to be able to map the query result to documents, scalars or other JSON serializable types.
With `AdvancedSqlQueryAsync` / `AdvancedSqlQuery` it is even possible to return multiple documents, objects and scalars as a tuple. Currently up to three result types can be queried for.

The following rules must be followed when doing queries with `AdvancedSqlQueryAsync` / `AdvancedSqlQuery`:

- If a document should be returned, the SQL `SELECT` statement must contain all the columns required by Marten to build
  the document in the correct order. Which columns are needed depends on the session type and if any meta data are
  mapped to the document.
- When having multiple return types, the columns required for each type must be enclosed in a SQL `ROW` statement.
- For non-document types the column `data` must return the JSON that will be deserialized to this type.

For document types the correct order of columns in the result is:

1. `id` - must always be present, except for `QuerySession`
2. `data` - must always be present
3. `mt_doc_type` - must be present only with document hierarchies
4. `mt_version` - only when versioning is enabled
5. `mt_last_modified` - only if this metadata is enabled 
6. `mt_created_at` - only if this metadata is enabled
7. `correlation_id` - only if this metadata is enabled
8. `causation_id` - only if this metadata is enabled
9. `last_modified_by` - only if this metadata is enabled
10. `mt_deleted` - only if this metadata is enabled
11. `mt_deleted_at` - only if this metadata is enabled

You can always check the correct result column order, by inspecting the command text created from a Linq query: `var commandText = session.Query<T>().ToCommand().CommandText;`

Querying for a simple scalar value can be done like this:

<!-- snippet: sample_advanced_sql_query_single_scalar -->
<a id='snippet-sample_advanced_sql_query_single_scalar'></a>
```cs
var schema = session.DocumentStore.Options.Schema;
var name = (await session.AdvancedSqlQueryAsync<string>(
    $"select data ->> 'Name' from {schema.For<DocWithMeta>()} limit 1",
    CancellationToken.None)).First();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L25-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_single_scalar' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or for multiple scalars returned as a tuple:

<!-- snippet: sample_advanced_sql_query_multiple_scalars -->
<a id='snippet-sample_advanced_sql_query_multiple_scalars'></a>
```cs
var (number,text, boolean) = (await session.AdvancedSqlQueryAsync<int, string, bool>(
    "select row(5), row('foo'), row(true) from (values(1)) as dummy",
    CancellationToken.None)).First();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L38-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_multiple_scalars' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for any arbitrary JSON that will get deserialized:

<!-- snippet: sample_advanced_sql_query_json_object -->
<a id='snippet-sample_advanced_sql_query_json_object'></a>
```cs
var result = (await session.AdvancedSqlQueryAsync<Foo, Bar>(
    "select row(json_build_object('Name', 'foo')), row(json_build_object('Name', 'bar')) from (values(1)) as dummy",
    CancellationToken.None)).First();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L52-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_json_object' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Querying for documents requires to return the correct columns:

<!-- snippet: sample_advanced_sql_query_documents -->
<a id='snippet-sample_advanced_sql_query_documents'></a>
```cs
var schema = session.DocumentStore.Options.Schema;
var docs = await session.AdvancedSqlQueryAsync<DocWithoutMeta>(
    $"select id, data from {schema.For<DocWithoutMeta>()} order by data ->> 'Name'",
    CancellationToken.None);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L68-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_documents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If metadata are available, remember to add the correct metadata columns to the result. The order of the columns is
important!:

<!-- snippet: sample_advanced_sql_query_documents_with_metadata -->
<a id='snippet-sample_advanced_sql_query_documents_with_metadata'></a>
```cs
var schema = session.DocumentStore.Options.Schema;
var doc = (await session.AdvancedSqlQueryAsync<DocWithMeta>(
    $"select id, data, mt_version from {schema.For<DocWithMeta>()} where data ->> 'Name' = 'Max'",
    CancellationToken.None)).First();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L85-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_documents_with_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for multiple related documents and scalar, e.g. for paging:

<!-- snippet: sample_advanced_sql_query_related_documents_and_scalar -->
<a id='snippet-sample_advanced_sql_query_related_documents_and_scalar'></a>
```cs
session.Store(new DocWithMeta { Id = 1, Name = "Max" });
session.Store(new DocDetailsWithMeta { Id = 1, Detail = "Likes bees" });
session.Store(new DocWithMeta { Id = 2, Name = "Michael" });
session.Store(new DocDetailsWithMeta { Id = 2, Detail = "Is a good chess player" });
session.Store(new DocWithMeta { Id = 3, Name = "Anne" });
session.Store(new DocDetailsWithMeta { Id = 3, Detail = "Hates soap operas" });
session.Store(new DocWithMeta { Id = 4, Name = "Beatrix" });
session.Store(new DocDetailsWithMeta { Id = 4, Detail = "Likes to cook" });
await session.SaveChangesAsync();

var schema = session.DocumentStore.Options.Schema;
IReadOnlyList<(DocWithMeta doc, DocDetailsWithMeta detail, long totalResults)> results =
    await session.AdvancedSqlQueryAsync<DocWithMeta, DocDetailsWithMeta, long>(
        $"""
        select
          row(a.id, a.data, a.mt_version),
          row(b.id, b.data, b.mt_version),
          row(count(*) over())
        from
          {schema.For<DocWithMeta>()} a
        left join
          {schema.For<DocDetailsWithMeta>()} b on a.id = b.id
        where
          (a.data ->> 'Id')::int > 1
        order by
          a.data ->> 'Name'
        limit 2
        """,
        CancellationToken.None);

results.Count.ShouldBe(2);
results[0].totalResults.ShouldBe(3);
results[0].doc.Name.ShouldBe("Anne");
results[0].detail.Detail.ShouldBe("Hates soap operas");
results[1].doc.Name.ShouldBe("Beatrix");
results[1].detail.Detail.ShouldBe("Likes to cook");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/advanced_sql_query.cs#L100-L137' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_advanced_sql_query_related_documents_and_scalar' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For sync queries you can use the `AdvancedSqlQuery<T>(...)` overloads.
