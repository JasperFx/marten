<!--Title:Querying Documents with Postgresql SQL-->
<!--Url:sql-->

It's not too hard to imagine a scenario where the Linq querying support is either inadequate or you just want full control over the SQL for the purpose of optimizing a query. For that reason, Marten supports the `IQuerySession/IDocumentSession.Query<T>(sql)` method that allows you to supply the SQL yourself.

In its easiest form, you just supply the SQL to the right of the FROM clause (_select data from [table] [your code]_) as in this sample below.

<[sample:query_with_only_the_where_clause]>

The actual JSONB data will always be a field called "data" in the database. If Marten does not spot a "SELECT" in the sql, it will fill in the "select data from mt_doc_type" SELECT and FROM clauses of the sql query for you.

To completely specify the sql, you'll need to know the table name matching your document type. By default, it'll be "mt_doc_[name of the class]."

<[sample:use_all_your_own_sql]>

The `Query<T>(sql)` mechanism will also allow you to use parameterized sql like so:

<[sample:using_parameterized_sql]>


The best resource for this topic might just be [the unit tests](https://github.com/JasperFx/Marten/blob/master/src/Marten.Testing/query_by_sql_where_clause_Tests.cs).


