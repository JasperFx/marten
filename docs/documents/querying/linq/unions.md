# Querying C# Union Members

::: info
C# 15 unions exist on .NET 11 only. On `net10.0`, no type is a union and nothing on this page applies.
:::

On .NET 11, a document member can be a C# union. System.Text.Json writes a union as its active case, so the stored
JSON is the case itself, with no wrapper. When two cases are JSON objects, System.Text.Json needs a classifier to read
the union back:

<!-- snippet: sample_union_document_types -->
<a id='snippet-sample_union_document_types'></a>
```cs
public record Card(string Last4);

public record Cash(decimal Amount);

// Both cases are JSON objects, so System.Text.Json needs a classifier to read the union back
[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union Payment(Card, Cash);

public class Invoice
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Payment Payment { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Unions/union_documentation_samples.cs#L13-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_union_document_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying a Union Member {#querying-a-union-member}

LINQ reaches the case through the union's `Value` property. `x.Payment is Card` does not compile in an expression
tree, so write `x.Payment.Value is Card`:

<!-- snippet: sample_querying_union_members -->
<a id='snippet-sample_querying_union_members'></a>
```cs
public async Task querying_union_members(IQuerySession session)
{
    // Which case the union holds. Reach the case through .Value:
    // `x.Payment is Card` does not compile in an expression tree
    var byCard = await session.Query<Invoice>().Where(x => x.Payment.Value is Card).ToListAsync();

    // A member of one case. Rows holding the other case never match
    var card1234 = await session.Query<Invoice>()
        .Where(x => ((Card)x.Payment.Value!).Last4 == "1234")
        .ToListAsync();

    var bigCash = await session.Query<Invoice>()
        .Where(x => (x.Payment.Value as Cash)!.Amount > 50m)
        .ToListAsync();

    // The default union holds no case
    var unpaid = await session.Query<Invoice>().Where(x => x.Payment.Value == null).ToListAsync();

    // Order by, and project, a member of a case
    var cashByAmount = await session.Query<Invoice>()
        .Where(x => x.Payment.Value is Cash)
        .OrderByDescending(x => ((Cash)x.Payment.Value!).Amount)
        .Select(x => x.Name)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Unions/union_documentation_samples.cs#L32-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_union_members' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten translates:

- A type test on the case: `x.Payment.Value is Card`, and its negation.
- A member of one case, through a cast or `as`: `((Card)x.Payment.Value).Last4`, `(x.Payment.Value as Cash).Amount`.
  A row that holds another case never matches, even with `!=`, and the member reads as `null` on it.
- `x.Payment.Value == null` and `!= null`, for the default union that holds no case.
- `(x.Payment.Value as Card) != null`, which is the same as the type test.
- A primitive case read as itself: `x.Code.Value is int`, `(int)x.Code.Value > 3`, `(string)x.Code.Value == "5"`.
- A nullable union, `x.Maybe.Value.Value is Card`, and a union on a nested object.
- `OrderBy()` and `Select()` of a member of a case.

## What Marten Refuses {#what-marten-refuses}

Marten throws `BadLinqExpressionException` rather than return rows that look right and are not:

- A type that is not one of the union's cases.
- Cases the stored JSON cannot tell apart: two cases with the same shape, two cases both written as a JSON number,
  or a case that can be written exactly like another one. Whether two cases look alike depends on your serializer
  options, such as leaving `null` properties out.
- A case that is itself a union.
- The union itself compared to a value other than `null`, ordered by, or used as a `GroupBy()` key.
- A member read off `Value` without a cast that names the case.
- A union inside a child collection predicate, such as `x.Lines.Any(l => l.Payment.Value is Card)`.
- A patch path through a case, such as `Set(x => ((Card)x.Payment.Value).Last4, "2222")`. Patch the whole union
  instead.
- The Newtonsoft serializer.
