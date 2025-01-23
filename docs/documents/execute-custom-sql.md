# Execute custom SQL in session

Use `QueueSqlCommand(string sql, params object[] parameterValues)` method to register and execute any custom/arbitrary SQL commands with the underlying unit of work, as part of the batched commands within `IDocumentSession`. 

`?` placeholders can be used to denote parameter values. Postgres [type casts `::`](https://www.postgresql.org/docs/15/sql-expressions.html#SQL-SYNTAX-TYPE-CASTS) can be applied to the parameter if needed. Alternatively named parameters can be used by passing in an anonymous object or a dictionary.

<!-- snippet: sample_QueueSqlCommand -->
<a id='snippet-sample_QueueSqlCommand'></a>
```cs
theSession.QueueSqlCommand("insert into names (name) values ('Jeremy')");
theSession.QueueSqlCommand("insert into names (name) values ('Babu')");
theSession.Store(Target.Random());
theSession.QueueSqlCommand("insert into names (name) values ('Oskar')");
theSession.Store(Target.Random());
var json = "{ \"answer\": 42 }";
theSession.QueueSqlCommand("insert into data (raw_value) values (?::jsonb)", json);
var parameters = new { newName = "Hawx" };
theSession.QueueSqlCommand("insert into names (name) values (@newName)", parameters);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/executing_arbitrary_sql_as_part_of_transaction.cs#L39-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_QueueSqlCommand' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
There are a lot of caveats with using named parameters. Most importantly they cannot be mixed with positional parameters, so be careful when composing multiple queries together. They are also "global" meaning that they will match the parameter name anywhere in the sql command, not just the line you added alongside the parameters.

Under the hood, postgres does not support named parameters. [Npgsql supports named parameters](https://www.npgsql.org/doc/basic-usage.html#positional-and-named-placeholders) by parsing and replacing positional parameters as named parameters. Parsing is not bullet-proof and has a performance impact.

Overall, you should avoid using named parameters unless you really need to.
:::
