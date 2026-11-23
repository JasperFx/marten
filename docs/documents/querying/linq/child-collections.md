# Querying within Child Collections

## Quantifier Operations within Child Collections {#quantifier-operations}

Marten supports the `Any()`, `All()` and `Contains()`
[quantifier operations](https://learn.microsoft.com/en-us/dotnet/csharp/linq/standard-query-operators/quantifier-operations)
within child collections.

The following code sample demonstrates the supported Linq patterns for collection searching:

<!-- snippet: sample_searching_within_child_collections -->
<a id='snippet-sample_searching_within_child_collections'></a>
```cs
public class ClassWithChildCollections
{
    public Guid Id;

    public IList<User> Users = new List<User>();
    public Company[] Companies = [];

    public string[] Names;
    public IList<string> NameList;
    public List<string> NameList2;
}

public void searching(IDocumentStore store)
{
    using (var session = store.QuerySession())
    {
        var searchNames = new string[] { "Ben", "Luke" };

        session.Query<ClassWithChildCollections>()
            // Where collections of deep objects
            .Where(x => x.Companies.Any(_ => _.Name == "Jeremy"))

            // Where for Contains() on array of simple types
            .Where(x => x.Names.Contains("Corey"))

            // Where for Contains() on IList<T> of simple types
            .Where(x => x.NameList.Contains("Phillip"))

            // Where for Contains() on List<T> of simple types
            .Where(x => x.NameList2.Contains("Jens"))

            // Where for Any(element == value) on simple types
            .Where(x => x.Names.Any(_ => _ == "Phillip"))

            // Where any element is one of a set of values from your code.
            // The collection on the document can be an array or a List<T>
            .Where(x => x.Names.Any(_ => searchNames.Contains(_)))

            // Where every element matches
            .Where(x => x.Companies.All(_ => _.Name.StartsWith("J")));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Searching_Within_Child_Collections.cs#L10-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_child_collections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can search on equality of multiple fields or properties within the child collection
using the `&&` operator:

<!-- snippet: sample_any-query-through-child-collection-with-and -->
<a id='snippet-sample_any-query-through-child-collection-with-and'></a>
```cs
var results =(await  theSession
    .Query<Target>()
    .Where(x => x.Children.Any(_ => _.Number == 6 && _.Double == -1))
    .ToListAsync());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/query_against_child_collections.cs#L179-L186' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any-query-through-child-collection-with-and' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Finally, you can query for child collections that do **not** contain a value:

<!-- snippet: sample_negated-contains -->
<a id='snippet-sample_negated-contains'></a>
```cs
(await theSession.Query<DocWithArrays>().CountAsync(x => !x.Strings.Contains("c")))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_561_negation_of_query_on_contains.cs#L33-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_negated-contains-1'></a>
```cs
(await theSession.Query<DocWithArrays>().CountAsync(x => !x.Strings.Contains("c")))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_561_negation_of_query_on_contains.cs#L81-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Nested Any() {#nested-any}

An `Any()` can contain another `Any()` over a collection of the child, to any depth, and the inner predicate can be
combined with conditions on the outer element:

<!-- snippet: sample_nested_any -->
<a id='snippet-sample_nested_any'></a>
```cs
public async Task nested_any(IQuerySession session)
{
    // A team with a green squad that has a player named Bill
    var teams = await session.Query<Team>()
        .Where(x => x.Squads.Any(s => s.Color == "Green" && s.Players.Any(p => p.Name == "Bill")))
        .ToListAsync();

    // A team with any squad that has any player at all
    var staffed = await session.Query<Team>()
        .Where(x => x.Squads.Any(s => s.Players.Any()))
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/child_collections_documentation_samples.cs#L71-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_nested_any' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## How Marten Translates Collection Predicates <Badge type="tip" text="9.15" /> {#how-marten-translates-collection-predicates}

Marten picks the most index-friendly SQL strategy it can for a `Where()` clause that filters
against a child collection, in this order:

| Predicate shape | SQL strategy | GIN index eligible? |
| --- | --- | --- |
| Equalities combined with `&&`, e.g. `x.Lines.Any(l => l.Name == "a" && l.Number == 2)` | JSONB containment: `data -> 'Lines' @> '[{"Name":"a","Number":2}]'` | Yes |
| Equalities combined with `\|\|`, e.g. `x.Lines.Any(l => l.Name == "a" \|\| l.Name == "b")` | OR of containment filters (`BitmapOr` over the index) | Yes |
| `Contains()` of a whole element, e.g. `x.Lines.Contains(line)` | JSONB containment: `d.data @> '{"Lines":[{...serialized element...}]}'`. Note: matches elements *containing* every serialized member of the argument, so documents written with extra/evolved properties still match | Yes, including whole-document `GinIndexJsonData()` indexes |
| Other comparisons (`>`, `<`, `>=`, `<=`, `!=`) and mixes, e.g. `x.Lines.Any(l => l.Name == "a" && l.Number > 5)` | JSONPath: `jsonb_path_exists(d.data, '$.Lines[*] ? (@.Name == $val1 && @.Number > $val2)', :params)` | No, but a single scan with a cheap per-row predicate |
| Case-sensitive `StartsWith()` | JSONPath `starts with $var` — fully parameterized, works in compiled queries | No |
| `Contains()`/`EndsWith()` on strings and case-insensitive comparisons | JSONPath `like_regex` with the (escaped) search text embedded in the SQL. **Not usable in compiled queries** — the value cannot be re-bound, and Marten fails loudly if you try | No |
| Aggregates in predicates, e.g. `x.Lines.Sum(l => l.Number) > 100` (also `Min`/`Max`/`Average`, and parameterless overloads on scalar collections) | Correlated scalar subquery: `(SELECT COALESCE(SUM(...), 0) FROM ... ) > :arg`. `Sum()` over an empty collection is 0 like LINQ-to-objects; `Min`/`Max`/`Average` over an empty collection simply fail the comparison (C# would throw) | No |
| `Count(predicate)`, e.g. `x.Lines.Count(l => l.IsActive) > 2` | JSONPath: `jsonb_array_length(jsonb_path_query_array(d.data, '$.Lines[*] ? (...)', :params))`. The same expression is available inside a `Select()` projection — see [Counting a Child Collection](/documents/querying/linq/projections#counting-a-child-collection) | No |
| `All(predicate)` for any jsonpath-capable predicate | `NOT jsonb_path_exists(d.data, '$.Lines[*] ? (!(...))')` — vacuously true on empty collections, and elements with null/missing members fail string predicates just like in LINQ-to-objects | No |
| Indexing into a collection, e.g. `x.Lines[0].Name == "x"`, `x.Lines.ElementAt(1).Number > 5` | Plain JSON path navigation: `data -> 'Lines' -> 0 ->> 'Name'`. Out-of-range indexes yield SQL NULL and simply don't match | Via computed/duplicated indexes only |
| Anything else (member-to-member comparisons, `DateTime` values) | Correlated `EXISTS (SELECT 1 FROM ...)` over the exploded elements | No |

A couple of practical notes:

- The `Any()` containment strategies (the first two rows) compare against `data -> 'CollectionName'`, so a GIN
  index needs to be an *expression* index on that same expression to be used, e.g.
  `CREATE INDEX ON mt_doc_order USING gin ((data -> 'Lines') jsonb_path_ops)`. A whole-document
  index from `GinIndexJsonData()` will not accelerate them. `Contains()` of a whole element is different: it
  compares against `d.data`, so a whole-document index does accelerate it.
- The JSONPath strategy passes all comparison values through the `vars` argument of
  `jsonb_path_exists()`, so the SQL stays fully parameterized and works with compiled queries.
- JSONPath comparisons follow C# semantics more closely than the older
  common-table-expression strategy did: comparing a `null` member with `!=` matches, and range
  comparisons against `null` or missing members do not.

## Querying within Value IEnumerables {#querying-within-value-ienumerables}

Marten can do "contains" searches within arrays, `List<T>` and `IList<T>` of primitive values like strings or numbers:

<!-- snippet: sample_query_against_string_array -->
<a id='snippet-sample_query_against_string_array'></a>
```cs
public async Task query_against_string_array()
{
    var doc1 = new DocWithArrays { Strings = ["a", "b", "c"] };
    var doc2 = new DocWithArrays { Strings = ["c", "d", "e"] };
    var doc3 = new DocWithArrays { Strings = ["d", "e", "f"] };

    theSession.Store(doc1);
    theSession.Store(doc2);
    theSession.Store(doc3);

    await theSession.SaveChangesAsync();

    (await theSession.Query<DocWithArrays>().Where(x => x.Strings.Contains("c")).ToListAsync())
        .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/query_against_child_collections.cs#L544-L562' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_string_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten also allows you to query over IEnumerables using the Any method for equality (similar to Contains):

<!-- snippet: sample_query_any_string_array -->
<a id='snippet-sample_query_any_string_array'></a>
```cs
[Fact]
public async Task query_against_number_list_with_any()
{
    var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
    var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
    var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7 } };
    var doc4 = new DocWithLists { Numbers = new List<int> { } };

    theSession.Store(doc1, doc2, doc3, doc4);

    await theSession.SaveChangesAsync();

    (await theSession.Query<DocWithLists>().Where(x => x.Numbers.Any(_ => _ == 3)).ToListAsync())
        .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);

    // Or without any predicate
    (await theSession.Query<DocWithLists>()
        .CountAsync(x => x.Numbers.Any())).ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/query_against_child_collections.cs#L660-L684' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_any_string_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query against the `Count()` or `Length` of a child collection with the normal comparison
operators (`==`, `>`, `>=`, etc.):

<!-- snippet: sample_query_against_number_list_with_count_method -->
<a id='snippet-sample_query_against_number_list_with_count_method'></a>
```cs
[Fact]
public async Task query_against_number_list_with_count_method()
{
    var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
    var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
    var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7, 8 } };

    theSession.Store(doc1);
    theSession.Store(doc2);
    theSession.Store(doc3);

    await theSession.SaveChangesAsync();

    (await theSession.Query<DocWithLists>()
        .SingleAsync(x => x.Numbers.Count() == 4)).Id.ShouldBe(doc3.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/query_against_child_collections.cs#L686-L707' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_number_list_with_count_method' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Reading an Element by Position {#reading-an-element-by-position}

An array or list on the document can be indexed in `Where()`, `OrderBy()`/`ThenBy()` and `Select()`. That works
with an array index, a `List<T>` indexer or `ElementAt()`. The index can be a literal or any value computed on the
client, like a captured variable. Indexes can be chained through jagged arrays and nested lists, and can be followed
by a member of the element:

<!-- snippet: sample_reading_an_element_by_position -->
<a id='snippet-sample_reading_an_element_by_position'></a>
```cs
public async Task reading_an_element_by_position(IQuerySession session)
{
    var row = 1;

    var hits = await session.Query<Plan>()
        .Where(x => x.Grid[row][0] > 3 && x.Steps[0].Name == "start")
        .OrderBy(x => x.Scores[2])
        .Select(x => new { x.Id, First = x.Tags[0], Cell = x.Grid[1][1] })
        .ToListAsync();

    // ElementAt() reads by position too
    var second = await session.Query<Plan>().Where(x => x.Steps.ElementAt(1).Name == "build").ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/child_collections_documentation_samples.cs#L53-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_reading_an_element_by_position' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Each index becomes a `jsonb` array lookup by position, such as `d.data -> 'Lines' -> 0 ->> 'ItemName'`. Two things
work differently from LINQ to Objects:

- An index past the end of the stored array reads as `null` rather than throwing. A comparison against it does not
  match, it sorts last in ascending order, and selecting it returns `null`, or the default value of a value type.
- A negative index throws `BadLinqExpressionException`. PostgreSQL would read it from the end of the array, which is
  not what it means in C#.

`bool[]` and `List<bool>` elements can be read the same way, `x.Flags[0]`.

## Querying Dictionaries {#querying-dictionaries}

A dictionary member is stored as a JSON object. Marten translates `ContainsKey()`, `Keys` and `Values` with
`Contains()` or `Any()`, `Any()` over the entries, `Count`, and the indexer, in `Where()` and in `OrderBy()`.
`SelectMany()` can flatten the keys or the values:

<!-- snippet: sample_querying_dictionaries -->
<a id='snippet-sample_querying_dictionaries'></a>
```cs
public async Task querying_dictionaries(IQuerySession session)
{
    // Target.StringDict is an IDictionary<string, string>
    var hasKey = await session.Query<Target>().Where(x => x.StringDict.ContainsKey("key0")).ToListAsync();
    var hasValue = await session.Query<Target>().Where(x => x.StringDict.Values.Contains("value3"))
        .ToListAsync();
    var someKey = await session.Query<Target>().Where(x => x.StringDict.Keys.Any(k => k.EndsWith("3")))
        .ToListAsync();
    var someEntry = await session.Query<Target>().Where(x => x.StringDict.Any(p => p.Value == "value2"))
        .ToListAsync();
    var bigOnes = await session.Query<Target>().Where(x => x.StringDict.Count > 2).ToListAsync();

    // Contains() of a whole entry needs the IDictionary<TKey, TValue> interface
    var pair = new KeyValuePair<string, string>("key0", "value0");
    var hasPair = await session.Query<Target>().Where(x => x.StringDict.Contains(pair)).ToListAsync();

    // Flatten the keys or the values of every document
    var allValues = await session.Query<Target>().SelectMany(x => x.StringDict.Values).ToListAsync();

    // Machine.Tags is a Dictionary<string, string>. The indexer reads one entry,
    // in Where() and in OrderBy()
    var redMachines = await session.Query<Machine>()
        .Where(x => x.Tags["color"] == "red")
        .OrderBy(x => x.Tags["zone"])
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/ChildCollections/child_collections_documentation_samples.cs#L88-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_dictionaries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## IsOneOf {#isoneof}

`IsOneOf()` extension can be used to query for documents having
a field or property matching one of many supplied values. It works like `Contains()` over a local collection, which
is described in [Contains() over a Local Collection](/documents/querying/linq/operators#contains-over-a-local-collection):

<!-- snippet: sample_is_one_of -->
<a id='snippet-sample_is_one_of'></a>
```cs
// Finds all SuperUser's whose role is either
// Admin, Supervisor, or Director
var users = session.Query<SuperUser>()
    .Where(x => x.Role.IsOneOf("Admin", "Supervisor", "Director"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IsOneOfExamples.cs#L11-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_one_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

On a collection member, `IsOneOf()` finds the documents whose collection holds at least one of the values. On a
string collection it becomes the `?|` operator, which a GIN index with the default `jsonb_ops` operator class can
serve:

<!-- snippet: sample_is_one_of_array -->
<a id='snippet-sample_is_one_of_array'></a>
```cs
// Finds all UserWithNicknames's whose nicknames matches either "Melinder" or "Norrland"

var nickNames = new[] {"Melinder", "Norrland"};

var users = session.Query<UserWithNicknames>()
    .Where(x => x.Nicknames.IsOneOf(nickNames));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IsOneOfExamples.cs#L35-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_one_of_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To find one of for a list you can use this strategy:

<!-- snippet: sample_is_one_of_list -->
<a id='snippet-sample_is_one_of_list'></a>
```cs
// Finds all SuperUser's whose role is either
// Admin, Supervisor, or Director
var listOfRoles = new List<string> {"Admin", "Supervisor", "Director"};

var users = session.Query<SuperUser>()
    .Where(x => x.Role.IsOneOf(listOfRoles));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IsOneOfExamples.cs#L22-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_one_of_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## In {#in}

`In()` extension works exactly the same as `IsOneOf()`. It was introduced as syntactic sugar to ease RavenDB transition:

<!-- snippet: sample_in -->
<a id='snippet-sample_in'></a>
```cs
// Finds all SuperUser's whose role is either
// Admin, Supervisor, or Director
var users = session.Query<SuperUser>()
    .Where(x => x.Role.In("Admin", "Supervisor", "Director"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/InExamples.cs#L11-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_in' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To find one of for an array you can use this strategy:

<!-- snippet: sample_in_array -->
<a id='snippet-sample_in_array'></a>
```cs
// Finds all UserWithNicknames's whose nicknames matches either "Melinder" or "Norrland"

var nickNames = new[] {"Melinder", "Norrland"};

var users = session.Query<UserWithNicknames>()
    .Where(x => x.Nicknames.In(nickNames));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/InExamples.cs#L35-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_in_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To find one of for a list you can use this strategy:

<!-- snippet: sample_in_list -->
<a id='snippet-sample_in_list'></a>
```cs
// Finds all SuperUser's whose role is either
// Admin, Supervisor, or Director
var listOfRoles = new List<string> {"Admin", "Supervisor", "Director"};

var users = session.Query<SuperUser>()
    .Where(x => x.Role.In(listOfRoles));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/InExamples.cs#L22-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_in_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## IsSupersetOf {#issupersetof}

<!-- snippet: sample_is_superset_of -->
<a id='snippet-sample_is_superset_of'></a>
```cs
// Finds all Posts whose Tags is superset of
// c#, json, or postgres
var posts = theSession.Query<Post>()
    .Where(x => x.Tags.IsSupersetOf("c#", "json", "postgres"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/is_super_set_of_operator.cs#L15-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_superset_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## IsSubsetOf {#issubsetof}

<!-- snippet: sample_is_subset_of -->
<a id='snippet-sample_is_subset_of'></a>
```cs
// Finds all Posts whose Tags is subset of
// c#, json, or postgres
var posts = theSession.Query<Post>()
    .Where(x => x.Tags.IsSubsetOf("c#", "json", "postgres"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/is_subset_of_operator.cs#L34-L40' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_subset_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
