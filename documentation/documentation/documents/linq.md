<!--Title:Querying Documents with Linq-->
<!--Url:linq-->

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

[This is still outstanding](https://github.com/JasperFx/Marten/issues/34). The current thinking is to use the Postgresql containment operator for doing
`Any(x => ...)` type queries within child collections.

## Searching for NULL Values

Regardless of your feelings about _NULL_, they do exist in databases and Marten allows you to search for documents that have (or don't have) null values:

<[sample:query_by_nullable_types]>

## Deep Queries

Marten's Linq support will allow you to make "deep" searches on properties of properties (or fields):

<[sample:deep_nested_properties]>

## Searching on String Fields

Marten supports a subset of the common substring searches:

<[sample:searching_within_string_fields]>

Marten does not yet support case insensitive substring searches. We'd love pull requests!

## Count()

Marten supports the `IQueryable.Count()` method:

<[sample:using_count]>

## Ordering Results

Marten contains support for expressing ordering in both ascending and descending order in Linq queries:

<[sample:ordering-in-linq]>


## Take() and Skip() for Paging

For simple paging, Marten supports the `IQueryable.Take()` and `IQueryable.Skip()` methods:

<[sample:using_take_and_skip]>


## Searching for a Single Document

Marten supports the `IQueryable` methods for returning only a single document at a time:

<[sample:select_a_single_value]>


## Querying withing Value Arrays

As of now, Marten allows you to do "contains" searches within arrays of primitive values like string or numbers:

<[sample:query_against_string_array]>

## Searching with Boolean Flags

Linq queries against boolean properties can use shorthand mechanisms in `Where()` clauses like so:

<[sample:boolean_queries]>


## Querying within Child Collections

As of Marten v0.6, you can use rudimentary `Any()` searches on child collections, **but Marten can only
query for equality of fields or properties within the Any() subquery.**

<[sample:any-query-through-child-collections]>

You can search on equality of multiple fields or properties within the child collection
using the `&&` operator:

<[sample:any-query-through-child-collection-with-and]>


## Supported Types

At this point, Marten's Linq support has been tested against these .Net types:

1. String
1. Int32 & Int64 (int and long)
1. Decimal (float)
1. DateTime and DateTimeOffset
1. Enum values
1. Nullable<T> of all of the above types
1. Booleans