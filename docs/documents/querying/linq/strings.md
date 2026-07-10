# Searching on String Fields

Marten supports a subset of the common sub/string searches:

<!-- snippet: sample_searching_within_string_fields -->
<a id='snippet-sample_searching_within_string_fields'></a>
```cs
public void string_fields(IDocumentSession session)
{
    session.Query<Target>().Where(x => x.String.StartsWith("A"));
    session.Query<Target>().Where(x => x.String.EndsWith("Suffix"));

    session.Query<Target>().Where(x => x.String.Contains("something"));
    session.Query<Target>().Where(x => x.String.Equals("The same thing"));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L72-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten also supports case insensitive substring searches:

<!-- snippet: sample_searching_within_case_insensitive_string_fields -->
<a id='snippet-sample_searching_within_case_insensitive_string_fields'></a>
```cs
public void case_insensitive_string_fields(IDocumentSession session)
{
    session.Query<Target>().Where(x => x.String.StartsWith("A", StringComparison.OrdinalIgnoreCase));
    session.Query<Target>().Where(x => x.String.EndsWith("SuFfiX", StringComparison.OrdinalIgnoreCase));

    // using Marten.Util
    session.Query<Target>().Where(x => x.String.Contains("soMeThiNg", StringComparison.OrdinalIgnoreCase));

    session.Query<Target>().Where(x => x.String.Equals("ThE SaMe ThInG", StringComparison.OrdinalIgnoreCase));

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L84-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_case_insensitive_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A shorthand for case-insensitive string matching is provided through `EqualsIgnoreCase` (string extension method in *JasperFx.Core*):

<!-- snippet: sample_sample-linq-equalsignorecase -->
<a id='snippet-sample_sample-linq-equalsignorecase'></a>
```cs
(await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("abc"))).Id.ShouldBe(user1.Id);
(await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("aBc"))).Id.ShouldBe(user1.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/string_filtering.cs#L223-L228' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-linq-equalsignorecase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten translates `EqualsIgnoreCase` to a PostgreSQL case-insensitive pattern-match (`~~*`, equivalent to `ILIKE`) rather than a .NET culture-aware comparison.

## Regular Expressions <Badge type="tip" text="9.15" />

`Regex.IsMatch()` translates to the PostgreSQL regex match operator (`~`, or `~*` for
`RegexOptions.IgnoreCase` — the only supported option) on top-level members, and to a jsonpath
`like_regex` predicate inside collection filters:

```cs
// WHERE d.data ->> 'UserName' ~ :pattern
session.Query<User>().Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$"));

// jsonb_path_exists(d.data, '$.Lines[*] ? (@.ItemName like_regex "...")')
session.Query<Order>().Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "^item-1\\d$")));
```

Two caveats: PostgreSQL regular expressions are POSIX-flavored (`like_regex` is the SQL/JSON
XQuery subset), so exotic .NET-specific constructs may behave differently. And inside collection
filters the pattern is embedded in the SQL, so a compiled query cannot re-bind it between
executions — Marten fails loudly if a compiled query member feeds such a pattern. Top-level
`Regex.IsMatch()` stays fully parameterized and works in compiled queries.
