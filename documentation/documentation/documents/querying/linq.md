<!--Title:Querying Documents with Linq-->
<!--Url:linq-->

TODO(Split this topic up, and use ST specs)

Marten uses the [Relinq library](https://github.com/re-motion/Relinq) to support a subset of the normal Linq operators. Linq queries are done with
Marten using the `IQuerySession.Query<T>()` or `IDocumentSession.Query<T>()` method to return an [IQueryable](https://msdn.microsoft.com/en-us/library/system.linq.iqueryable(v=vs.100).aspx) for the document type `T`.

<[sample:querying_with_linq]>

To query for all documents of a type - not that you would do this very often outside of testing - use the `Query<T>()` method like this:

<[sample:query_for_all]>


## Basic Operators

Since you usually don't want to pull down the entire database at one time, Marten supports these basic operators in Linq searches:

<[sample:query_by_basic_operators]>

## And and Or Queries

Right now, Marten supports both _and_ and _or_ queries with Linq:

<[sample:querying_with_and_or_or]>

## Searching within Child Collections

As of v0.7, Marten supports simple `Any()` queries within child collections, *but only for checking
equality* of members of the child collection elements (this feature uses the [Postgresql JSONB containment operator](http://www.postgresql.org/docs/9.5/static/datatype-json.html) to compose the underlying SQL).

Marten will also allow you to use the `Contains` method to search within arrays or lists of simple elements like strings.

The following code sample demonstrates the supported Linq patterns for collection searching:

<[sample:searching_within_child_collections]>

You can search on equality of multiple fields or properties within the child collection
using the `&&` operator:

<[sample:any-query-through-child-collection-with-and]>

## Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<[sample:query_by_nullable_types]>

## Deep Queries

Marten's Linq support will allow you to make "deep" searches on properties of properties (or fields):

<[sample:deep_nested_properties]>

## Searching on String Fields

Marten supports a subset of the common sub/string searches:

<[sample:searching_within_string_fields]>

Marten also supports case insensitive substring searches:

<[sample:searching_within_case_insensitive_string_fields]>

## Count()

Marten supports the `IQueryable.Count()` method:

<[sample:using_count]>

## Min()

Marten supports the `IQueryable.Min()` method:

<[sample:using_min]>

## Max()

Marten supports the `IQueryable.Max()` method:

<[sample:using_max]>

## Average()

Marten supports the `IQueryable.Average()` method:

<[sample:using_average]>

## Sum()

Marten supports the `IQueryable.Sum()` method:

<[sample:using_sum]>

## Ordering Results

Marten contains support for expressing ordering in both ascending and descending order in Linq queries:

<[sample:ordering-in-linq]>


## Take() and Skip() for Paging

For simple paging, Marten supports the `IQueryable.Take()` and `IQueryable.Skip()` methods:

<[sample:using_take_and_skip]>


## Searching for a Single Document

Marten supports the `IQueryable` methods for returning only a single document at a time:

<[sample:select_a_single_value]>


## Querying within Value IEnumerables

As of now, Marten allows you to do "contains" searches within Arrays, Lists & ILists of primitive values like string or numbers:

<[sample:query_against_string_array]>

Marten also allows you to query over IEnumerables using the Any method for equality (similar to Contains):

<[sample:query_any_string_array]>

## Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<[sample:boolean_queries]>

## IsOneOf

Marten v0.8 added a new extension method called `IsOneOf()` that can be used to query for documents having
a field or property matching one of many supplied values:

<[sample:is_one_of]>

## Modulo Queries

Marten v0.8 added the ability to use the modulo operator in Linq queries:

<[sample:querying-with-modulo]>


## Supported Types

At this point, Marten's Linq support has been tested against these .Net types:

1. String
1. Int32 & Int64 (int and long)
1. Decimal (float)
1. DateTime and DateTimeOffset
1. Enum values
1. Nullable<T> of all of the above types
1. Booleans
