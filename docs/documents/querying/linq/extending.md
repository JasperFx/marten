# Extending Marten's Linq Support

::: tip INFO
The Linq parsing and translation to Postgresql JSONB queries, not to mention Marten's own helpers and model, are pretty involved and this guide isn't exhaustive. Please feel free to ask for help in [Marten's Discord channel](https://discord.gg/WMxrvegf8H) linked above if there's any Linq customization or extension that you need.
:::

Marten allows you to add Linq parsing and querying support for your own custom methods.
Using the (admittedly contrived) example from Marten's tests, say that you want to reuse a small part of a `Where()` clause across
different queries for "IsBlue()." First, write the method you want to be recognized by Marten's Linq support:

<!-- snippet: sample_IsBlue -->
<a id='snippet-sample_IsBlue'></a>
```cs
public class IsBlue: IMethodCallParser
{
    private static readonly PropertyInfo _property = ReflectionHelper.GetProperty<ColorTarget>(x => x.Color);

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(CustomExtensions.IsBlue);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var locator = memberCollection.MemberFor(expression).TypedLocator;

        return new WhereFragment($"{locator} = 'Blue'");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/custom_linq_extensions.cs#L82-L102' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_IsBlue' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note a couple things here:

1. If you're only using the method for Linq queries, it technically doesn't have to be implemented and never actually runs
1. The methods do not have to be extension methods, but we're guessing that will be the most common usage of this

Now, to create a custom Linq parser for the `IsBlue()` method, you need to create a custom implementation of the `IMethodCallParser`
interface shown below:

<<< @/../src/Marten/Linq/Parsing/IMethodCallParser.cs#sample_IMethodCallParser

The `IMethodCallParser` interface needs to match on method expressions that it could parse, and be able to turn the Linq expression into
part of a Postgresql "where" clause. The custom Linq parser for `IsBlue()` is shown below:

<!-- snippet: sample_custom-extension-for-linq -->
<a id='snippet-sample_custom-extension-for-linq'></a>
```cs
public static bool IsBlue(this string value)
{
    return value == "Blue";
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/custom_linq_extensions.cs#L72-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom-extension-for-linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, to plug in our new parser, we can add that to the `StoreOptions` object that we use to bootstrap a new `DocumentStore` as shown below:

<!-- snippet: sample_using_custom_linq_parser -->
<a id='snippet-sample_using_custom_linq_parser'></a>
```cs
[Fact]
public async Task query_with_custom_parser()
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);

        // IsBlue is a custom parser I used for testing this
        opts.Linq.MethodCallParsers.Add(new IsBlue());
        opts.AutoCreateSchemaObjects = AutoCreate.All;

        // This is just to isolate the test
        opts.DatabaseSchemaName = "isblue";
    });

    await store.Advanced.Clean.DeleteAllDocumentsAsync();

    var targets = new List<ColorTarget>();
    for (var i = 0; i < 25; i++)
    {
        targets.Add(new ColorTarget {Color = "Blue"});
        targets.Add(new ColorTarget {Color = "Green"});
        targets.Add(new ColorTarget {Color = "Red"});
    }

    var count = targets.Count(x => x.Color.IsBlue());

    targets.Each(x => x.Id = Guid.NewGuid());

    await store.BulkInsertAsync(targets.ToArray());

    using var session = store.QuerySession();
    session.Query<ColorTarget>().Count(x => x.Color.IsBlue())
        .ShouldBe(count);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/custom_linq_extensions.cs#L23-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_custom_linq_parser' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
