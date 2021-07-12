# Paging

For paged access to data, Marten provides `ToPagedList` and `ToPagedListAsync` extension methods on `IQueryable<T>`.

<!-- snippet: sample_to_paged_list -->
<a id='snippet-sample_to_paged_list'></a>
```cs
var pageNumber = 2;
var pageSize = 10;

var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);

// paged list also provides a list of helper properties to deal with pagination aspects
var totalItems = pagedList.TotalItemCount; // get total number records
var pageCount = pagedList.PageCount; // get number of pages
var isFirstPage = pagedList.IsFirstPage; // check if current page is first page
var isLastPages = pagedList.IsLastPage; // check if current page is last page
var hasNextPage = pagedList.HasNextPage; // check if there is next page
var hasPrevPage = pagedList.HasPreviousPage; // check if there is previous page
var firstItemOnPage = pagedList.FirstItemOnPage; // one-based index of first item in current page
var lastItemOnPage = pagedList.LastItemOnPage; // one-based index of last item in current page
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Pagination/pagedlist_queryable_extension_Tests.cs#L51-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_to_paged_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_to_paged_list_async -->
<a id='snippet-sample_to_paged_list_async'></a>
```cs
var pageNumber = 2;
var pageSize = 10;

var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Pagination/pagedlist_queryable_extension_Tests.cs#L75-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_to_paged_list_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to create you own paged queries, just use the `Take()` and `Skip()` Linq operators in combination with `Stats()`

<!-- snippet: sample_using-query-statistics -->
<a id='snippet-sample_using-query-statistics'></a>
```cs
[Fact]
public void can_get_the_total_in_results()
{
    var count = theSession.Query<Target>().Count(x => x.Number > 10);
    SpecificationExtensions.ShouldBeGreaterThan(count, 0);

    // We're going to use stats as an output
    // parameter to the call below, so we
    // have to declare the "stats" object
    // first
    QueryStatistics stats = null;

    var list = theSession
        .Query<Target>()
        .Stats(out stats)
        .Where(x => x.Number > 10).Take(5)
        .ToList();

    list.Any().ShouldBeTrue();

    // Now, the total results data should
    // be available
    stats.TotalResults.ShouldBe(count);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/invoking_query_with_statistics.cs#L165-L191' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-query-statistics' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For the sake of completeness, the SQL generated in the operation above by Marten would be:

```sql
select d.data, d.id, count(1) OVER() as total_rows from public.mt_doc_target as d
where CAST(d.data ->> 'Number' as integer) > :arg0 LIMIT 5
```

The `Stats()` Linq operator can be used in conjunction with `Include()` and within batch queries. Marten does not yet
support using `Stats()` within the compiled query.
