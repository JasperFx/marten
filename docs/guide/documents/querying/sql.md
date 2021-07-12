# Querying with Postgresql SQL

It's not too hard to imagine a scenario where the Linq querying support is either inadequate or you just want full control over the SQL for the purpose of optimizing a query. For that reason, Marten supports the `IQuerySession/IDocumentSession.Query<T>(sql)` and the `MatchesSql(sql)` methods that allow you to supply the SQL yourself.

In its easiest form, you just supply the SQL to the right of the FROM clause (_select data from [table] [your code]_) as in this sample below.

<!-- snippet: sample_query_with_only_the_where_clause -->
<a id='snippet-sample_query_with_only_the_where_clause'></a>
```cs
[Fact]
public void query_for_single_document()
{
    using (var session = theStore.OpenSession())
    {
        var u = new User {FirstName = "Jeremy", LastName = "Miller"};
        session.Store(u);
        session.SaveChanges();

        var user = session.Query<User>("where data ->> 'FirstName' = 'Jeremy'").Single();
        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L227-L244' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_only_the_where_clause' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The actual JSONB data will always be a field called "data" in the database. If Marten does not spot a "SELECT" in the sql, it will fill in the "select data from mt_doc_type" SELECT and FROM clauses of the sql query for you.

To completely specify the sql, you'll need to know the table name matching your document type. By default, it'll be "mt_doc_[name of the class]."

<!-- snippet: sample_use_all_your_own_sql -->
<a id='snippet-sample_use_all_your_own_sql'></a>
```cs
var user =
    session.Query<User>("select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'")
        .Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L290-L296' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_all_your_own_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Query<T>(sql)` mechanism will also allow you to use parameterized sql like so:

<!-- snippet: sample_using_parameterized_sql -->
<a id='snippet-sample_using_parameterized_sql'></a>
```cs
var user =
    session.Query<User>("where data ->> 'FirstName' = ? and data ->> 'LastName' = ?", "Jeremy",
            "Miller")
        .Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L126-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_parameterized_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to combine other Linq operations with your sql, you can use the `MatchesSql(sql)` method inside of your Linq query like so:

<!-- snippet: sample_query_with_matches_sql -->
<a id='snippet-sample_query_with_matches_sql'></a>
```cs
[Fact]
public void query_with_matches_sql()
{
    using (var session = theStore.OpenSession())
    {
        var u = new User {FirstName = "Eric", LastName = "Smith"};
        session.Store(u);
        session.SaveChanges();

        var user = session.Query<User>().Where(x => x.MatchesSql("data->> 'FirstName' = ?", "Eric")).Single();
        user.LastName.ShouldBe("Smith");
        user.Id.ShouldBe(u.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L262-L279' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_matches_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The best resource for this topic might just be [the unit tests](https://github.com/JasperFx/Marten/blob/master/src/Marten.Testing/query_by_sql_where_clause_Tests.cs).

## Asynchronous Queries

You can also query asynchronously with user supplied SQL:

<!-- snippet: sample_using-queryasync -->
<a id='snippet-sample_using-queryasync'></a>
```cs
var users =
    await
        session.QueryAsync<User>(
            "select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'");
var user = users.Single();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L312-L320' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-queryasync' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Non-generic Overloads

The SQL queries described above can also be performed through the non-generic IQuerySession extensions, which allow for providing the document type during runtime. The sample below demonstrates this feature together with the C# `dynamic` type.

<!-- snippet: sample_sample-query-type-parameter-overload -->
<a id='snippet-sample_sample-query-type-parameter-overload'></a>
```cs
dynamic userFromDb = session.Query(user.GetType(), "where id = ?", user.Id).First();
dynamic companyFromDb = (await session.QueryAsync(typeof(Company), "where id = ?", CancellationToken.None, company.Id)).First();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_session_extension_Tests.cs#L23-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-query-type-parameter-overload' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Named Parameter Queries

Some of the Postgresql operators include the "?" character that Marten normally uses to denote an input parameter in user supplied queries.
To solve that conflict, Marten 1.2 introduces support for named parameters in the user supplied queries:

<!-- snippet: sample_query_by_two_named_parameters -->
<a id='snippet-sample_query_by_two_named_parameters'></a>
```cs
[Fact]
public void query_by_two_named_parameters()
{
    using (var session = theStore.OpenSession())
    {
        session.Store(new User {FirstName = "Jeremy", LastName = "Miller"});
        session.Store(new User {FirstName = "Lindsey", LastName = "Miller"});
        session.Store(new User {FirstName = "Max", LastName = "Miller"});
        session.Store(new User {FirstName = "Frank", LastName = "Zombo"});
        session.SaveChanges();
        var user =
            session.Query<User>("where data ->> 'FirstName' = :FirstName and data ->> 'LastName' = :LastName",
                    new {FirstName = "Jeremy", LastName = "Miller"})
                .Single();

        SpecificationExtensions.ShouldNotBeNull(user);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L139-L160' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_two_named_parameters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
