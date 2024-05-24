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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L58-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_string_fields' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L70-L83' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_case_insensitive_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A shorthand for case-insensitive string matching is provided through `EqualsIgnoreCase` (string extension method in *Baseline*):

<!-- snippet: sample_sample-linq-EqualsIgnoreCase -->
<a id='snippet-sample_sample-linq-equalsignorecase'></a>
```cs
query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("abc")).Id.ShouldBe(user1.Id);
query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("aBc")).Id.ShouldBe(user1.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/string_filtering.cs#L225-L230' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-linq-equalsignorecase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This defaults to `String.Equals` with `StringComparison.CurrentCultureIgnoreCase` as comparison type.
