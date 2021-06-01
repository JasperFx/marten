# Querying Documents with Linq

Marten uses the [Relinq library](https://github.com/re-motion/Relinq) to support a subset of the normal Linq operators. Linq queries are done with
Marten using the `IQuerySession.Query<T>()` or `IDocumentSession.Query<T>()` method to return an [IQueryable](https://msdn.microsoft.com/en-us/library/system.linq.iqueryable(v=vs.100).aspx) for the document type `T`.

<!-- snippet: sample_querying_with_linq -->
<!-- endSnippet -->

To query for all documents of a type - not that you would do this very often outside of testing - use the `Query<T>()` method like this:

<!-- snippet: sample_query_for_all -->
<!-- endSnippet -->

## Basic Operators

Since you usually don't want to pull down the entire database at one time, Marten supports these basic operators in Linq searches:

<!-- snippet: sample_query_by_basic_operators -->
<!-- endSnippet -->

## And and Or Queries

Right now, Marten supports both _and_ and _or_ queries with Linq:

<!-- snippet: sample_querying_with_and_or_or -->
<!-- endSnippet -->

## Searching within Child Collections

As of v0.7, Marten supports simple `Any()` queries within child collections, **but only for checking
equality** of members of the child collection elements (this feature uses the [Postgresql JSONB containment operator](http://www.postgresql.org/docs/9.5/static/datatype-json.html) to compose the underlying SQL).

Marten will also allow you to use the `Contains` method to search within arrays or lists of simple elements like strings.

The following code sample demonstrates the supported Linq patterns for collection searching:

<!-- snippet: sample_searching_within_child_collections -->
<!-- endSnippet -->

You can search on equality of multiple fields or properties within the child collection
using the `&&` operator:

<!-- snippet: sample_any-query-through-child-collection-with-and -->
<!-- endSnippet -->

Finally, you can query for child collections that do **not** contain a value:

<!-- snippet: sample_negated-contains -->
<!-- endSnippet -->

## Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<!-- snippet: sample_query_by_nullable_types -->
<!-- endSnippet -->

## Deep Queries

Marten's Linq support will allow you to make "deep" searches on properties of properties (or fields):

<!-- snippet: sample_deep_nested_properties -->
<!-- endSnippet -->

## Searching on String Fields

Marten supports a subset of the common sub/string searches:

<!-- snippet: sample_searching_within_string_fields -->
<!-- endSnippet -->

Marten also supports case insensitive substring searches:

<!-- snippet: sample_searching_within_case_insensitive_string_fields -->
<!-- endSnippet -->

A shorthand for case-insensitive string matching is provided through `EqualsIgnoreCase` (string extension method in *Baseline*):

<!-- snippet: sample_sample-linq-EqualsIgnoreCase -->
<!-- endSnippet -->

This defaults to `String.Equals` with `StringComparison.CurrentCultureIgnoreCase` as comparison type.

## Count()

Marten supports the `IQueryable.Count()` method:

<!-- snippet: sample_using_count -->
<!-- endSnippet -->

## Min()

Marten supports the `IQueryable.Min()` method:

<!-- snippet: sample_using_min -->
<!-- endSnippet -->

## Max()

Marten supports the `IQueryable.Max()` method:

<!-- snippet: sample_using_max -->
<!-- endSnippet -->

## Average()

Marten supports the `IQueryable.Average()` method:

<!-- snippet: sample_using_average -->
<!-- endSnippet -->

## Sum()

Marten supports the `IQueryable.Sum()` method:

<!-- snippet: sample_using_sum -->
<!-- endSnippet -->

## Ordering Results

Marten contains support for expressing ordering in both ascending and descending order in Linq queries:

<!-- snippet: sample_ordering-in-linq -->
<!-- endSnippet -->

## Take() and Skip() for Paging

For simple paging, Marten supports the `IQueryable.Take()` and `IQueryable.Skip()` methods:

<!-- snippet: sample_using_take_and_skip -->
<!-- endSnippet -->

## Searching for a Single Document

Marten supports the `IQueryable` methods for returning only a single document at a time:

<!-- snippet: sample_select_a_single_value -->
<!-- endSnippet -->

## Querying within Value IEnumerables

As of now, Marten allows you to do "contains" searches within Arrays, Lists & ILists of primitive values like string or numbers:

<!-- snippet: sample_query_against_string_array -->
<!-- endSnippet -->

Marten also allows you to query over IEnumerables using the Any method for equality (similar to Contains):

<!-- snippet: sample_query_any_string_array -->
<!-- endSnippet -->

As of 1.2, you can also query against the `Count()` or `Length` of a child collection with the normal comparison
operators (`==`, `>`, `>=`, etc.):

<!-- snippet: sample_query_against_number_list_with_count_method -->
<!-- endSnippet -->

## SelectMany()

Marten 1.2 adds the ability to use the `SelectMany()` operator to issue queries against child collections. You can use
`SelectMany()` against primitive collections like so:

<!-- snippet: sample_can_do_simple_select_many_against_simple_array -->
<!-- endSnippet -->

Or against collections of child documents:

<!-- snippet: sample_using-select-many -->
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
<!-- endSnippet -->

Do note that the `Distinct()` keyword can be used with `Select()` transforms as well:

<!-- snippet: sample_get_distinct_numbers -->
<!-- endSnippet -->

## Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<!-- snippet: sample_boolean_queries -->
<!-- endSnippet -->

## Use MatchesSql(sql) to search using raw SQL

Combine your Linq queries with raw SQL using the `MatchesSql(sql)` method like so:

<!-- snippet: sample_query_with_matches_sql -->
<!-- endSnippet -->

## IsOneOf

`IsOneOf()` extension can be used to query for documents having
a field or property matching one of many supplied values:

<!-- snippet: sample_is_one_of -->
<!-- endSnippet -->

To find one of for an array you can use this strategy:

<!-- snippet: sample_is_one_of_array -->
<!-- endSnippet -->

To find one of for a list you can use this strategy:

<!-- snippet: sample_is_one_of_list -->
<!-- endSnippet -->

## In

`In()` extension works exactly the same as `IsOneOf()`. It was introduced as syntactic sugar to ease RavenDB transition:

<!-- snippet: sample_in -->
<!-- endSnippet -->

To find one of for an array you can use this strategy:

<!-- snippet: sample_in_array -->
<!-- endSnippet -->

To find one of for a list you can use this strategy:

<!-- snippet: sample_in_list -->
<!-- endSnippet -->

## IsSupersetOf

<!-- snippet: sample_is_superset_of -->
<!-- endSnippet -->

## IsSubsetOf

<!-- snippet: sample_is_subset_of -->
<!-- endSnippet -->

## Modulo Queries

Marten has the ability to use the modulo operator in Linq queries:

<!-- snippet: sample_querying-with-modulo -->
<!-- endSnippet -->

## AnyTenant

Query data from all tenants using `AnyTenant` method.

<!-- snippet: sample_any_tenant -->
<!-- endSnippet -->

## TenantIsOneOf

Use `TenantIsOneOf` to query on a selected list of tenants.

<!-- snippet: sample_tenant_is_one_of -->
<!-- endSnippet -->

## Text Search

Postgres contains built in [Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). They enable the possibility to do more sophisticated searching through text fields. Marten gives possibility to define (full text indexes)(/guide/documents/configuration/full_text) and perform queries on them.
Currently three types of full Text Search functions are supported:

* regular Search (to_tsquery)

<!-- snippet: sample_search_in_query_sample -->
<!-- endSnippet -->

* plain text Search (plainto_tsquery)

<!-- snippet: sample_plain_search_in_query_sample -->
<!-- endSnippet -->

* phrase Search (phraseto_tsquery)

<!-- snippet: sample_phrase_search_in_query_sample -->
<!-- endSnippet -->

* web-style Search (websearch_to_tsquery, [supported from Postgres 11+](https://www.postgresql.org/docs/11/textsearch-controls.html)

<!-- snippet: sample_web_search_in_query_sample -->
<!-- endSnippet -->

All types of Text Searches can be combined with other Linq queries

<!-- snippet: sample_text_search_combined_with_other_query_sample -->
<!-- endSnippet -->

They allow also to specify language (regConfig) of the text search query (by default `english` is being used)

<!-- snippet: sample_text_search_with_non_default_regConfig_sample -->
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
