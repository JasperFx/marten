# Querying within Child Collections

::: tip
Marten V4 greatly improved Marten's abilities to query within child collections of documents
:::

## Quantifier Operations within Child Collections

Marten supports the `Any()` and `Contains()` [quantifier operations](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/quantifier-operations) within child collections.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_against_child_collections_integrated_Tests.cs#L93-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_any-query-through-child-collection-with-and' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Finally, you can query for child collections that do **not** contain a value:

<!-- snippet: sample_negated-contains -->
<a id='snippet-sample_negated-contains'></a>
```cs
theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Bugs/Bug_561_negation_of_query_on_contains.cs#L34-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_negated-contains-1'></a>
```cs
theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
    .ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Bugs/Bug_561_negation_of_query_on_contains.cs#L74-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_negated-contains-1' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_against_child_collections_integrated_Tests.cs#L417-L434' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_string_array' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_against_child_collections_integrated_Tests.cs#L521-L542' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_any_string_array' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_against_child_collections_integrated_Tests.cs#L544-L562' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_against_number_list_with_count_method' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_with_IsSuperSetOf_Tests.cs#L17-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_superset_of' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/Linq/query_with_IsSubsetOf_Tests.cs#L37-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_is_subset_of' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
