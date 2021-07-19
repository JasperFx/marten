# Querying Documents with Linq

Marten uses the [Relinq library](https://github.com/re-motion/Relinq) to support a subset of the normal Linq operators. Linq queries are done with
Marten using the `IQuerySession.Query<T>()` or `IDocumentSession.Query<T>()` method to return an [IQueryable](https://msdn.microsoft.com/en-us/library/system.linq.iqueryable(v=vs.100).aspx) for the document type `T`.

<!-- snippet: sample_querying_with_linq -->
<a id='snippet-sample_querying_with_linq'></a>
```cs
/// <summary>
/// Use Linq operators to query the documents
/// stored in Postgresql
/// </summary>
/// <typeparam name="T"></typeparam>
/// <returns></returns>
IMartenQueryable<T> Query<T>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/IQuerySession.cs#L84-L93' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To query for all documents of a type - not that you would do this very often outside of testing - use the `Query<T>()` method like this:

<!-- snippet: sample_query_for_all -->
<a id='snippet-sample_query_for_all'></a>
```cs
public void get_all_documents_of_a_type(IDocumentSession session)
{
    // Calling ToArray() just forces the query to be executed
    var targets = session.Query<Target>().ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L10-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_all' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Basic Operators

Since you usually don't want to pull down the entire database at one time, Marten supports these basic operators in Linq searches:

<!-- snippet: sample_query_by_basic_operators -->
<a id='snippet-sample_query_by_basic_operators'></a>
```cs
public void basic_operators(IDocumentSession session)
{
    // Field equals a value
    session.Query<Target>().Where(x => x.Number == 5);

    // Field does not equal a value
    session.Query<Target>().Where(x => x.Number != 5);

    // Field compared to values
    session.Query<Target>().Where(x => x.Number > 5);
    session.Query<Target>().Where(x => x.Number >= 5);
    session.Query<Target>().Where(x => x.Number < 5);
    session.Query<Target>().Where(x => x.Number <= 5);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L19-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_basic_operators' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## And and Or Queries

Right now, Marten supports both _and_ and _or_ queries with Linq:

<!-- snippet: sample_querying_with_and_or_or -->
<a id='snippet-sample_querying_with_and_or_or'></a>
```cs
public void and_or(IDocumentSession session)
{
    // AND queries
    session.Query<Target>().Where(x => x.Number > 0 && x.Number <= 5);

    // OR queries
    session.Query<Target>().Where(x => x.Number == 5 || x.Date == DateTime.Today);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L37-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_with_and_or_or' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Searching within Child Collections

As of v0.7, Marten supports simple `Any()` queries within child collections, **but only for checking
equality** of members of the child collection elements (this feature uses the [Postgresql JSONB containment operator](http://www.postgresql.org/docs/9.5/static/datatype-json.html) to compose the underlying SQL).

Marten will also allow you to use the `Contains` method to search within arrays or lists of simple elements like strings.

The following code sample demonstrates the supported Linq patterns for collection searching:

<!-- snippet: sample_searching_within_child_collections -->
<a id='snippet-sample_searching_within_child_collections'></a>
```cs
public class ClassWithChildCollections
{
    public Guid Id;

    public IList<User> Users = new List<User>();
    public Company[] Companies = new Company[0];

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

            // Where for Contains() on List<T> of simple types
            .Where(x => x.NameList.Contains("Phillip"))

            // Where for Contains() on IList<T> of simple types
            .Where(x => x.NameList2.Contains("Jens"))

            // Where for Any(element == value) on simple types
            .Where(x => x.Names.Any(_ => _ == "Phillip"))

            // The Contains() operator on subqueries within Any() searches
            // only supports constant array of String or Guid expressions.
            // Both the property being searched (Names) and the values
            // being compared (searchNames) need to be arrays.
            .Where(x => x.Names.Any(_ => searchNames.Contains(_)));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Searching_Within_Child_Collections.cs#L10-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_child_collections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can search on equality of multiple fields or properties within the child collection
using the `&&` operator:

<!-- snippet: sample_any-query-through-child-collection-with-and -->
<a id='snippet-sample_any-query-through-child-collection-with-and'></a>
```cs
var results = theSession
    .Query<Target>()
    .Where(x => x.Children.Any(_ => _.Number == 6 && _.Double == -1))
    .ToArray();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_against_child_collections_integrated_Tests.cs#L95-L100' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any-query-through-child-collection-with-and' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Finally, you can query for child collections that do **not** contain a value:

<!-- snippet: sample_negated-contains -->
<a id='snippet-sample_negated-contains'></a>
```cs
theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Bugs/Bug_561_negation_of_query_on_contains.cs#L27-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_negated-contains-1'></a>
```cs
theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Bugs/Bug_561_negation_of_query_on_contains.cs#L67-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<!-- snippet: sample_query_by_nullable_types -->
<a id='snippet-sample_query_by_nullable_types'></a>
```cs
public void query_by_nullable_type_nulls(IDocumentSession session)
{
    // You can use Nullable<T>.HasValue in Linq queries
    session.Query<Target>().Where(x => !x.NullableNumber.HasValue).ToArray();
    session.Query<Target>().Where(x => x.NullableNumber.HasValue).ToArray();

    // You can always search by field is NULL
    session.Query<Target>().Where(x => x.Inner == null);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L145-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_by_nullable_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Deep Queries

Marten's Linq support will allow you to make "deep" searches on properties of properties (or fields):

<!-- snippet: sample_deep_nested_properties -->
<a id='snippet-sample_deep_nested_properties'></a>
```cs
public void deep_queries(IDocumentSession session)
{
    session.Query<Target>().Where(x => x.Inner.Number == 3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L49-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deep_nested_properties' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Searching on String Fields

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L57-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_string_fields' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L69-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_searching_within_case_insensitive_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A shorthand for case-insensitive string matching is provided through `EqualsIgnoreCase` (string extension method in *Baseline*):

<!-- snippet: sample_sample-linq-EqualsIgnoreCase -->
<a id='snippet-sample_sample-linq-equalsignorecase'></a>
```cs
query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("abc")).Id.ShouldBe(user1.Id);
query.Query<User>().Single(x => x.UserName.EqualsIgnoreCase("aBc")).Id.ShouldBe(user1.Id);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/EqualsIgnoreCase_filtering.cs#L26-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-linq-equalsignorecase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This defaults to `String.Equals` with `StringComparison.CurrentCultureIgnoreCase` as comparison type.

## Count()

Marten supports the `IQueryable.Count()` method:

<!-- snippet: sample_using_count -->
<a id='snippet-sample_using_count'></a>
```cs
public void count_with_a_where_clause()
{
    // theSession is an IDocumentSession in this test
    theSession.Store(new Target { Number = 1 });
    theSession.Store(new Target { Number = 2 });
    theSession.Store(new Target { Number = 3 });
    theSession.Store(new Target { Number = 4 });
    theSession.Store(new Target { Number = 5 });
    theSession.Store(new Target { Number = 6 });
    theSession.SaveChanges();

    theSession.Query<Target>().Count(x => x.Number > 3).ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_queryable_count_Tests.cs#L103-L118' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_count' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Min()

Marten supports the `IQueryable.Min()` method:

<!-- snippet: sample_using_min -->
<a id='snippet-sample_using_min'></a>
```cs
[Fact]
public void get_min()
{
    theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
    theSession.Store(new Target { Color = Colors.Red, Number = 2 });
    theSession.Store(new Target { Color = Colors.Green, Number = -5 });
    theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

    theSession.SaveChanges();
    var minNumber = theSession.Query<Target>().Min(t => t.Number);
    minNumber.ShouldBe(-5);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_aggregate_functions.cs#L44-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_min' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Max()

Marten supports the `IQueryable.Max()` method:

<!-- snippet: sample_using_max -->
<a id='snippet-sample_using_max'></a>
```cs
[Fact]
public void get_max()
{
    theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
    theSession.Store(new Target { Color = Colors.Red, Number = 42 });
    theSession.Store(new Target { Color = Colors.Green, Number = 3 });
    theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

    theSession.SaveChanges();
    var maxNumber = theSession.Query<Target>().Max(t => t.Number);
    maxNumber.ShouldBe(42);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_aggregate_functions.cs#L16-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_max' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Average()

Marten supports the `IQueryable.Average()` method:

<!-- snippet: sample_using_average -->
<a id='snippet-sample_using_average'></a>
```cs
[Fact]
public void get_average()
{
    theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
    theSession.Store(new Target { Color = Colors.Red, Number = 2 });
    theSession.Store(new Target { Color = Colors.Green, Number = -5 });
    theSession.Store(new Target { Color = Colors.Blue, Number = 42 });

    theSession.SaveChanges();
    var average = theSession.Query<Target>().Average(t => t.Number);
    average.ShouldBe(10);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_aggregate_functions.cs#L72-L85' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_average' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Sum()

Marten supports the `IQueryable.Sum()` method:

<!-- snippet: sample_using_sum -->
<a id='snippet-sample_using_sum'></a>
```cs
[Fact]
public void get_sum_of_integers()
{
    theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
    theSession.Store(new Target { Color = Colors.Red, Number = 2 });
    theSession.Store(new Target { Color = Colors.Green, Number = 3 });
    theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

    theSession.SaveChanges();
    theSession.Query<Target>().Sum(x => x.Number)
        .ShouldBe(10);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_for_sum_Tests.cs#L14-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_sum' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Ordering Results

Marten contains support for expressing ordering in both ascending and descending order in Linq queries:

<!-- snippet: sample_ordering-in-linq -->
<a id='snippet-sample_ordering-in-linq'></a>
```cs
public void order_by(IDocumentSession session)
{
    // Sort in ascending order
    session.Query<Target>().OrderBy(x => x.Date);

    // Sort in descending order
    session.Query<Target>().OrderByDescending(x => x.Date);

    // You can use multiple order by's
    session.Query<Target>().OrderBy(x => x.Date).ThenBy(x => x.Number);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L83-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ordering-in-linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Take() and Skip() for Paging

For simple paging, Marten supports the `IQueryable.Take()` and `IQueryable.Skip()` methods:

<!-- snippet: sample_using_take_and_skip -->
<a id='snippet-sample_using_take_and_skip'></a>
```cs
public void using_take_and_skip(IDocumentSession session)
{
    // gets records 11-20 from the database
    session.Query<Target>().Skip(10).Take(10).OrderBy(x => x.Number).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L98-L105' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_take_and_skip' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Searching for a Single Document

Marten supports the `IQueryable` methods for returning only a single document at a time:

<!-- snippet: sample_select_a_single_value -->
<a id='snippet-sample_select_a_single_value'></a>
```cs
public void select_a_single_value(IDocumentSession session)
{
    // Single()/SingleOrDefault() will throw exceptions if more than
    // one result is returned from the database
    session.Query<Target>().Where(x => x.Number == 5).Single();
    session.Query<Target>().Where(x => x.Number == 5).SingleOrDefault();

    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).First();
    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).FirstOrDefault();

    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).Last();
    session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).LastOrDefault();

    // Using the query inside of Single/Last/First is supported as well
    session.Query<Target>().Single(x => x.Number == 5);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L107-L125' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_select_a_single_value' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying within Value IEnumerables

As of now, Marten allows you to do "contains" searches within Arrays, Lists & ILists of primitive values like string or numbers:

<!-- snippet: sample_query_against_string_array -->
<a id='snippet-sample_query_against_string_array'></a>
```cs
public void query_against_string_array()
{
    var doc1 = new DocWithArrays { Strings = new [] { "a", "b", "c" } };
    var doc2 = new DocWithArrays { Strings = new [] { "c", "d", "e" } };
    var doc3 = new DocWithArrays { Strings = new [] { "d", "e", "f" } };

    theSession.Store(doc1);
    theSession.Store(doc2);
    theSession.Store(doc3);

    theSession.SaveChanges();

    theSession.Query<DocWithArrays>().Where(x => x.Strings.Contains("c")).ToArray()
        .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_against_child_collections_integrated_Tests.cs#L419-L436' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_string_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten also allows you to query over IEnumerables using the Any method for equality (similar to Contains):

<!-- snippet: sample_query_any_string_array -->
<a id='snippet-sample_query_any_string_array'></a>
```cs
[Fact]
public void query_against_number_list_with_any()
{
    var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
    var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
    var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7 } };
    var doc4 = new DocWithLists { Numbers = new List<int> { } };

    theSession.Store(doc1, doc2, doc3, doc4);

    theSession.SaveChanges();

    theSession.Query<DocWithLists>().Where(x => x.Numbers.Any(_ => _ == 3)).ToArray()
        .Select(x => x.Id).ShouldHaveTheSameElementsAs(doc1.Id, doc2.Id);

    // Or without any predicate
    theSession.Query<DocWithLists>()
        .Count(x => x.Numbers.Any()).ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_against_child_collections_integrated_Tests.cs#L523-L544' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_any_string_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of 1.2, you can also query against the `Count()` or `Length` of a child collection with the normal comparison
operators (`==`, `>`, `>=`, etc.):

<!-- snippet: sample_query_against_number_list_with_count_method -->
<a id='snippet-sample_query_against_number_list_with_count_method'></a>
```cs
[Fact]
public void query_against_number_list_with_count_method()
{
    var doc1 = new DocWithLists { Numbers = new List<int> { 1, 2, 3 } };
    var doc2 = new DocWithLists { Numbers = new List<int> { 3, 4, 5 } };
    var doc3 = new DocWithLists { Numbers = new List<int> { 5, 6, 7, 8 } };

    theSession.Store(doc1);
    theSession.Store(doc2);
    theSession.Store(doc3);

    theSession.SaveChanges();

    theSession.Query<DocWithLists>()
        .Single(x => x.Numbers.Count() == 4).Id.ShouldBe(doc3.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_against_child_collections_integrated_Tests.cs#L546-L564' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_number_list_with_count_method' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## SelectMany()

Marten 1.2 adds the ability to use the `SelectMany()` operator to issue queries against child collections. You can use
`SelectMany()` against primitive collections like so:

<!-- snippet: sample_can_do_simple_select_many_against_simple_array -->
<a id='snippet-sample_can_do_simple_select_many_against_simple_array'></a>
```cs
[Fact]
public void can_do_simple_select_many_against_simple_array()
{
    var product1 = new Product {Tags = new[] {"a", "b", "c"}};
    var product2 = new Product {Tags = new[] {"b", "c", "d"}};
    var product3 = new Product {Tags = new[] {"d", "e", "f"}};

    using (var session = theStore.OpenSession())
    {
        session.Store(product1, product2, product3);
        session.SaveChanges();
    }

    using (var query = theStore.QuerySession())
    {
        var distinct = query.Query<Product>().SelectMany(x => x.Tags).Distinct().ToList();

        distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs("a", "b", "c", "d", "e", "f");

        var names = query.Query<Product>().SelectMany(x => x.Tags).ToList();
        names
            .Count().ShouldBe(9);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_select_many.cs#L20-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_can_do_simple_select_many_against_simple_array' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or against collections of child documents:

<!-- snippet: sample_using-select-many -->
<a id='snippet-sample_using-select-many'></a>
```cs
var results = query.Query<Target>()
    .SelectMany(x => x.Children)
    .Where(x => x.Flag)
    .OrderBy(x => x.Id)
    .Skip(20)
    .Take(15)
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_select_many.cs#L394-L402' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-select-many' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A few notes on the `SelectMany()` usage and limitations:

* As of 1.2, you are only able to use a single `SelectMany()` operator in a single Linq query. That limitation will be removed in 1.3.
* You can use any other Linq operator that Marten supports *after* the `SelectMany()` in a Linq query, including the `Stats()` and `Include()` operators
* `Take()` and `Skip()` operators in a Linq query that contains a `SelectMany()` operator will always apply to the child collection database
  rather than the parent document regardless of the order in which the operators appear in the Linq query
* You cannot use `SelectMany()` with both a `Distinct()` and a `Count()` operator at this point.

## Distinct()

New in Marten 1.2 is support for the Linq `Distinct()` operator:

<!-- snippet: sample_get_distinct_strings -->
<a id='snippet-sample_get_distinct_strings'></a>
```cs
[Fact]
public void get_distinct_string()
{
    theSession.Store(new Target {String = "one"});
    theSession.Store(new Target {String = "one"});
    theSession.Store(new Target {String = "two"});
    theSession.Store(new Target {String = "two"});
    theSession.Store(new Target {String = "three"});
    theSession.Store(new Target {String = "three"});

    theSession.SaveChanges();

    var queryable = theSession.Query<Target>().Select(x => x.String).Distinct();

    queryable.ToList().Count.ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_distinct_Tests.cs#L53-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_strings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that the `Distinct()` keyword can be used with `Select()` transforms as well:

<!-- snippet: sample_get_distinct_numbers -->
<a id='snippet-sample_get_distinct_numbers'></a>
```cs
[SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
public void get_distinct_numbers()
{
    theSession.Store(new Target {Number = 1, Decimal = 1.0M});
    theSession.Store(new Target {Number = 1, Decimal = 2.0M});
    theSession.Store(new Target {Number = 1, Decimal = 2.0M});
    theSession.Store(new Target {Number = 2, Decimal = 1.0M});
    theSession.Store(new Target {Number = 2, Decimal = 2.0M});
    theSession.Store(new Target {Number = 2, Decimal = 1.0M});

    theSession.SaveChanges();

    var queryable = theSession.Query<Target>().Select(x => new
    {
        x.Number,
        x.Decimal
    }).Distinct();

    queryable.ToList().Count.ShouldBe(4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_distinct_Tests.cs#L30-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_get_distinct_numbers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<!-- snippet: sample_boolean_queries -->
<a id='snippet-sample_boolean_queries'></a>
```cs
public void query_by_booleans(IDocumentSession session)
{
    // Flag is a boolean property.

    // Where Flag is true
    session.Query<Target>().Where(x => x.Flag).ToArray();
    // or
    session.Query<Target>().Where(x => x.Flag == true).ToArray();

    // Where Flag is false
    session.Query<Target>().Where(x => !x.Flag).ToArray();
    // or
    session.Query<Target>().Where(x => x.Flag == false).ToArray();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/LinqExamples.cs#L127-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_boolean_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Use MatchesSql(sql) to search using raw SQL

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/query_by_sql_where_clause_Tests.cs#L262-L279' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_matches_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## IsOneOf

`IsOneOf()` extension can be used to query for documents having
a field or property matching one of many supplied values:

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

To find one of for an array you can use this strategy:

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

## In

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

## IsSupersetOf

<!-- snippet: sample_is_superset_of -->
<a id='snippet-sample_is_superset_of'></a>
```cs
// Finds all Posts whose Tags is superset of
// c#, json, or postgres
var posts = theSession.Query<Post>()
    .Where(x => x.Tags.IsSupersetOf("c#", "json", "postgres"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_IsSuperSetOf_Tests.cs#L16-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_superset_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## IsSubsetOf

<!-- snippet: sample_is_subset_of -->
<a id='snippet-sample_is_subset_of'></a>
```cs
// Finds all Posts whose Tags is subset of
// c#, json, or postgres
var posts = theSession.Query<Post>()
    .Where(x => x.Tags.IsSubsetOf("c#", "json", "postgres"));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_IsSubsetOf_Tests.cs#L31-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_subset_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Modulo Queries

Marten has the ability to use the modulo operator in Linq queries:

<!-- snippet: sample_querying-with-modulo -->
<a id='snippet-sample_querying-with-modulo'></a>
```cs
[Fact]
public void use_modulo()
{
    theSession.Store(new Target{Color = Colors.Blue, Number = 1});
    theSession.Store(new Target{Color = Colors.Blue, Number = 2});
    theSession.Store(new Target{Color = Colors.Blue, Number = 3});
    theSession.Store(new Target{Color = Colors.Blue, Number = 4});
    theSession.Store(new Target{Color = Colors.Blue, Number = 5});
    theSession.Store(new Target{Color = Colors.Green, Number = 6});

    theSession.SaveChanges();

    theSession.Query<Target>().Where(x => x.Number % 2 == 0 && x.Color < Colors.Green).ToArray()
        .Select(x => x.Number)
        .ShouldHaveTheSameElementsAs(2, 4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_modulo_Tests.cs#L12-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying-with-modulo' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## AnyTenant

Query data from all tenants using `AnyTenant` method.

<!-- snippet: sample_any_tenant -->
<a id='snippet-sample_any_tenant'></a>
```cs
// query data across all tenants
var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/multi_tenancy.cs#L352-L356' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any_tenant' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## TenantIsOneOf

Use `TenantIsOneOf` to query on a selected list of tenants.

<!-- snippet: sample_tenant_is_one_of -->
<a id='snippet-sample_tenant_is_one_of'></a>
```cs
// query data for a selected list of tenants
var actual = query.Query<Target>().Where(x => x.TenantIsOneOf("Green", "Red") && x.Flag)
    .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/multi_tenancy.cs#L380-L384' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tenant_is_one_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Text Search

Postgres contains built in [Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). They enable the possibility to do more sophisticated searching through text fields. Marten gives possibility to define (full text indexes)(/guide/documents/configuration/full_text) and perform queries on them.
Currently three types of full Text Search functions are supported:

* regular Search (to_tsquery)

<!-- snippet: sample_search_in_query_sample -->
<a id='snippet-sample_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.Search("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L235-L239' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* plain text Search (plainto_tsquery)

<!-- snippet: sample_plain_search_in_query_sample -->
<a id='snippet-sample_plain_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PlainTextSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L262-L266' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_plain_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* phrase Search (phraseto_tsquery)

<!-- snippet: sample_phrase_search_in_query_sample -->
<a id='snippet-sample_phrase_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PhraseSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L289-L293' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_phrase_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* web-style Search (websearch_to_tsquery, [supported from Postgres 11+](https://www.postgresql.org/docs/11/textsearch-controls.html)

<!-- snippet: sample_web_search_in_query_sample -->
<a id='snippet-sample_web_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.WebStyleSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L316-L320' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_web_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

All types of Text Searches can be combined with other Linq queries

<!-- snippet: sample_text_search_combined_with_other_query_sample -->
<a id='snippet-sample_text_search_combined_with_other_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.Category == "LifeStyle")
    .Where(x => x.PhraseSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L344-L349' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_text_search_combined_with_other_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

They allow also to specify language (regConfig) of the text search query (by default `english` is being used)

<!-- snippet: sample_text_search_with_non_default_regConfig_sample -->
<a id='snippet-sample_text_search_with_non_default_regconfig_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PhraseSearch("somefilter", "italian"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L372-L376' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_text_search_with_non_default_regconfig_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Supported Types

At this point, Marten's Linq support has been tested against these .Net types:

1. `String`
1. `Int32` & `Int64` (`int` and `long`)
1. `Decimal` (float)
1. `DateTime` and `DateTimeOffset`
1. `Enum` values
1. `Nullable<T>` of all of the above types
1. `Boolean`
