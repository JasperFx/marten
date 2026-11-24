# Searching on String Fields

## Substring Searches {#substring-searches}

Marten translates `StartsWith()`, `EndsWith()`, `Contains()` and `Equals()` on a string member:

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

## Case-Insensitive Searches {#case-insensitive-searches}

Pass `StringComparison.OrdinalIgnoreCase` for a case-insensitive search:

<!-- snippet: sample_searching_within_case_insensitive_string_fields -->
<a id='snippet-sample_searching_within_case_insensitive_string_fields'></a>
```cs
public void case_insensitive_string_fields(IDocumentSession session)
{
    session.Query<Target>().Where(x => x.String.StartsWith("A", StringComparison.OrdinalIgnoreCase));
    session.Query<Target>().Where(x => x.String.EndsWith("SuFfiX", StringComparison.OrdinalIgnoreCase));

    session.Query<Target>().Where(x => x.String.Contains("soMeThiNg", StringComparison.OrdinalIgnoreCase));

    session.Query<Target>().Where(x => x.String.Equals("ThE SaMe ThInG", StringComparison.OrdinalIgnoreCase));

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L84-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_case_insensitive_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`EqualsIgnoreCase()`, a string extension method in *JasperFx.Core*, is a shorthand for a case-insensitive equality
test:

<!-- snippet: sample_sample-linq-equalsignorecase -->
<a id='snippet-sample_sample-linq-equalsignorecase'></a>
```cs
(await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("abc"))).Id.ShouldBe(user1.Id);
(await query.Query<User>().SingleAsync(x => x.UserName.EqualsIgnoreCase("aBc"))).Id.ShouldBe(user1.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/string_filtering.cs#L223-L228' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-linq-equalsignorecase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten translates `EqualsIgnoreCase()` to PostgreSQL's `ILIKE`, not to a .NET culture-aware comparison. The `%` and
`_` wildcards in the value are escaped, so they match literally.

## Null, Empty and Whitespace {#null-empty-and-whitespace}

`string.IsNullOrEmpty()` and `string.IsNullOrWhiteSpace()` work in `Where()`. `IsNullOrWhiteSpace()` matches `null`
and any value made only of whitespace, including tabs and line breaks:

<!-- snippet: sample_string_is_null_or_empty -->
<a id='snippet-sample_string_is_null_or_empty'></a>
```cs
public async Task is_null_or_empty(IQuerySession session)
{
    var noNickname = await session.Query<User>().Where(x => string.IsNullOrEmpty(x.Nickname)).ToListAsync();

    // Matches null and any run of whitespace, including tabs and line breaks
    var blank = await session.Query<User>().Where(x => string.IsNullOrWhiteSpace(x.Nickname)).ToListAsync();

    var hasNickname = await session.Query<User>().Where(x => !string.IsNullOrWhiteSpace(x.Nickname))
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/strings_documentation_samples.cs#L16-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_string_is_null_or_empty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Case, Trim and Length {#case-trim-and-length}

`ToLower()`, `ToUpper()`, `ToLowerInvariant()`, `ToUpperInvariant()` and `Trim()` on a string member become
PostgreSQL's `lower()`, `upper()` and `trim()`. `Length` becomes `length()`, which counts characters where .NET counts
UTF-16 code units, so the two differ for a string holding an emoji or another character outside the Basic Multilingual
Plane:

<!-- snippet: sample_string_case_trim_and_length -->
<a id='snippet-sample_string_case_trim_and_length'></a>
```cs
public async Task case_trim_and_length(IQuerySession session)
{
    var red = await session.Query<Target>().Where(x => x.String.ToLower() == "red").ToListAsync();
    var green = await session.Query<Target>().Where(x => x.String.ToUpperInvariant() == "GREEN").ToListAsync();
    var padded = await session.Query<Target>().Where(x => x.PaddedString.Trim() == "red").ToListAsync();
    var shortOnes = await session.Query<Target>().Where(x => x.String.Length < 5).ToListAsync();

    // The result of ToLower() or ToUpper() can feed IsOneOf()
    var either = await session.Query<Target>().Where(x => x.String.ToLower().IsOneOf("red", "green"))
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/strings_documentation_samples.cs#L30-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_string_case_trim_and_length' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`ToLower()`, `ToUpper()` and `Length` also work in a `Select()`, and on the strings a `SelectMany()` flattens. See
[Computed Values in Select()](/documents/querying/linq/projections#computed-values-in-select).

## Comparing Strings {#comparing-strings}

`string.Compare(a, b)` and `a.CompareTo(b)` compared with 0 become a comparison of the two strings. PostgreSQL orders
text by the database's collation, which can differ from .NET's ordering:

<!-- snippet: sample_comparing_strings -->
<a id='snippet-sample_comparing_strings'></a>
```cs
public async Task comparing_strings(IQuerySession session)
{
    // string.Compare() and CompareTo() compared with 0 become >, <, >= or <= on the text
    var afterCherry = await session.Query<Target>().Where(x => string.Compare(x.String, "Cherry") > 0)
        .ToListAsync();
    var upToBanana = await session.Query<Target>().Where(x => x.String.CompareTo("Banana") <= 0)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/strings_documentation_samples.cs#L45-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_comparing_strings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Regular Expressions <Badge type="tip" text="9.15" /> {#regular-expressions}

`Regex.IsMatch()` translates to the PostgreSQL regex match operator, `~`, on a member of the document.
`RegexOptions.IgnoreCase` is the only option Marten supports, and it becomes `~*`. Any other option throws
`BadLinqExpressionException`:

<!-- snippet: sample_regex_is_match -->
<a id='snippet-sample_regex_is_match'></a>
```cs
// WHERE d.data ->> 'UserName' ~ :pattern
var matching = await theSession.Query<RegexUser>()
    .Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$"))
    .ToListAsync();

// RegexOptions.IgnoreCase is the only option, and becomes ~*
var insensitive = await theSession.Query<RegexUser>()
    .Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$", RegexOptions.IgnoreCase))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/regex_is_match.cs#L107-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_regex_is_match' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Inside a child collection predicate, `Regex.IsMatch()` becomes a JSONPath `like_regex` filter:

<!-- snippet: sample_regex_is_match_in_a_child_collection -->
<a id='snippet-sample_regex_is_match_in_a_child_collection'></a>
```cs
public async Task regex_is_match_in_a_child_collection(IQuerySession session)
{
    // jsonb_path_exists(d.data, '$.Lines[*] ? (@.ItemName like_regex "...")')
    var orders = await session.Query<JsonPathOrder>()
        .Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "^item-1\\d$")))
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/strings_documentation_samples.cs#L57-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_regex_is_match_in_a_child_collection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Two caveats. PostgreSQL regular expressions are POSIX-flavored, and `like_regex` uses the SQL/JSON XQuery subset, so
.NET-specific constructs may behave differently. And inside a child collection predicate the pattern is written into
the SQL, so a compiled query cannot re-bind it between executions. Marten throws if a compiled query member feeds such
a pattern. A top-level `Regex.IsMatch()` stays parameterized and works in compiled queries.
