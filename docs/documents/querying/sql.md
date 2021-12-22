# Querying with Postgresql SQL

::: tip
In all the code samples on this page, the `session` variable is of type
`IQuerySession`.
:::

The Marten project strives to make the Linq provider robust and performant, but if there's ever a time when the Linq support is insufficient, you can drop down to using raw SQL to query documents in Marten.

Here's the simplest possible usage to query for `User` documents with a `WHERE` clause:

<!-- snippet: sample_query_for_whole_document_by_where_clause -->
<a id='snippet-sample_query_for_whole_document_by_where_clause'></a>
```cs
var millers = session
    .Query<User>("where data ->> 'LastName' = 'Miller'");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/QueryBySql.cs#L10-L15' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_whole_document_by_where_clause' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or with parameterized SQL:

<!-- snippet: sample_query_with_sql_and_parameters -->
<a id='snippet-sample_query_with_sql_and_parameters'></a>
```cs
var millers = session
    .Query<User>("where data ->> 'LastName' = ?", "Miller");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/QueryBySql.cs#L20-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_sql_and_parameters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And finally asynchronously:

<!-- snippet: sample_query_with_sql_async -->
<a id='snippet-sample_query_with_sql_async'></a>
```cs
var millers = await session
    .QueryAsync<User>("where data ->> 'LastName' = ?", "Miller");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/QueryBySql.cs#L30-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_sql_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

All of the samples so far are selecting the whole `User` document and merely supplying
a SQL `WHERE` clause, but you can also invoke scalar functions or SQL transforms against
a document body, but in that case you will need to supply the full SQL statement like this:

<!-- snippet: sample_query_by_full_sql -->
<a id='snippet-sample_query_by_full_sql'></a>
```cs
var sumResults = await session
    .QueryAsync<int>("select count(*) from mt_doc_target");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql.cs#L352-L357' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_full_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The basic rules for how Marten handles user-supplied queries are:

* The `T` argument to `Query<T>()/QueryAsync<T>()` denotes the return value of each item
* If the `T` is a simple, scalar value like a .Net `int`, the data is handled by reading the first
  field of the returned data
* If the `T` is not a simple type, Marten will try to read the first field with the JSON serializer
  for the current `DocumentStore`
* If the SQL starts with the `SELECT` keyword (and it's not case sensitive), the SQL supplied is used verbatim
* If the supplied SQL does not start with a `SELECT` keyword, Marten assumes that the `T` is a document
  type and queries that document table with `select data from [the document table name] [user supplied where clause]`
* You can omit the `WHERE` keyword and Marten will add that automatically if it's missing
* You can also use SQL `ORDER BY` clauses
