# Mixing Raw SQL with Linq

## MatchesSql() {#matchessql}

Combine your Linq queries with raw SQL using the `MatchesSql(sql)` method like so:

<!-- snippet: sample_query_with_matches_sql -->
<a id='snippet-sample_query_with_matches_sql'></a>
```cs
[Fact]
public async Task query_with_matches_sql()
{
    using var session = theStore.LightweightSession();
    var u = new User { FirstName = "Eric", LastName = "Smith" };
    session.Store(u);
    await session.SaveChangesAsync();

    var user = (await session.Query<User>().Where(x => x.MatchesSql("data->> 'FirstName' = ?", "Eric")).SingleAsync());
    user.LastName.ShouldBe("Smith");
    user.Id.ShouldBe(u.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_by_sql.cs#L367-L382' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_matches_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Each `?` in the SQL is replaced by the next argument, which Marten sends as a parameter. Use `d` as the alias of the
document table.

## JSONPath {#jsonpath}

A `?` placeholder does not suit [JSONPath](https://www.postgresql.org/docs/current/functions-json.html#FUNCTIONS-SQLJSON-PATH)
expressions, which use `?` themselves. Pass a different placeholder character as the first argument of
`MatchesSql()`, or use `MatchesJsonPath()`, which uses `^` as its placeholder:

<!-- snippet: sample_using_matchesjsonpath -->
<a id='snippet-sample_using_matchesjsonpath'></a>
```cs
var results2 = await theSession
    .Query<Target>().Where(x => x.MatchesSql('^', "d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
    .ToListAsync();

// older approach that only supports the ^ placeholder
var results3 = await theSession
    .Query<Target>().Where(x => x.MatchesJsonPath("d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_3087_using_JsonPath_with_MatchesSql.cs#L19-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_matchesjsonpath' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The JSONPath itself can be a parameter, which keeps a value from your code out of the SQL text:

<!-- snippet: sample_matches_json_path_with_a_parameter -->
<a id='snippet-sample_matches_json_path_with_a_parameter'></a>
```cs
// The JSONPath is bound as a parameter in place of ^
var baz = await session.Query<User>()
    .Where(x => x.MatchesJsonPath("d.data @? ^::jsonpath", "$ ? (@.UserName == \"baz\")"))
    .Select(x => x.UserName)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/matches_sql_queries.cs#L53-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_matches_json_path_with_a_parameter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`MatchesJsonPath()` throws `BadLinqExpressionException` when the number of `^` placeholders and the number of
arguments differ. A `^`
inside a JSONPath literal, such as a regular expression anchor in `like_regex "^ba"`, counts as a placeholder.

## SQL Fragments {#sql-fragments}

`MatchesSql()` also takes an `ISqlFragment` from Weasel, such as a `WhereFragment`, or a `CompoundWhereFragment` that
combines several:

<!-- snippet: sample_matches_sql_with_a_fragment -->
<a id='snippet-sample_matches_sql_with_a_fragment'></a>
```cs
// WhereFragment and CompoundWhereFragment come from Weasel.Postgresql.SqlGeneration
var whereFragment = CompoundWhereFragment.And();
whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "baz"));
whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "jack"));

var names = await session.Query<User>()
    .Where(x => x.MatchesSql(whereFragment))
    .OrderBy(x => x.UserName)
    .Select(x => x.UserName)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/matches_sql_queries.cs#L157-L168' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_matches_sql_with_a_fragment' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
