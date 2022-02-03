# Mixing Raw SQL with Linq

Combine your Linq queries with raw SQL using the `MatchesSql(sql)` method like so:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_by_sql.cs#L279-L296' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_matches_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
