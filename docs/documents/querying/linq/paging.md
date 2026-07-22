# Paging

For paged access to data, Marten provides `ToPagedList` and `ToPagedListAsync` extension methods on `IQueryable<T>`.

<!-- snippet: sample_to_paged_list -->
<a id='snippet-sample_to_paged_list'></a>
```cs
var pageNumber = 2;
var pageSize = 10;

var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize);

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/statistics_and_paged_list.cs#L217-L232' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_to_paged_list' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_to_paged_list_async -->
<a id='snippet-sample_to_paged_list_async'></a>
```cs
var pageNumber = 2;
var pageSize = 10;

var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/statistics_and_paged_list.cs#L241-L246' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_to_paged_list_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For total row count, by default it internally uses `Stats()` based query which is a window function using `count(*) OVER()`. This works well for small to medium datasets but won't perform well for large dataset with millions of records. To deal with large datasets, `ToPagedList` and `ToPagedListAsync` support a method override to pass boolean `useCountQuery` as `true` which will run a separate `count(*)` query for the total rows. See an example below:

<!-- snippet: sample_to_paged_list_seperate_count_query -->
<a id='snippet-sample_to_paged_list_seperate_count_query'></a>
```cs
var pageNumber = 2;
var pageSize = 10;
var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize, true);

// paged list also provides a list of helper properties to deal with pagination aspects
var totalItems = pagedList.TotalItemCount; // get total number records
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/statistics_and_paged_list.cs#L254-L261' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_to_paged_list_seperate_count_query' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to construct you own paged queries without using `ToPagedList`, just use the `Take()` and `Skip()` Linq operators in combination with `Stats()`

<!-- snippet: sample_using-query-statistics -->
<a id='snippet-sample_using-query-statistics'></a>
```cs
[Fact]
public async Task can_get_the_total_in_results()
{
    var count = (await theSession.Query<Target>().CountAsync(x => x.Number > 10));
    count.ShouldBeGreaterThan(0);

    // We're going to use stats as an output
    // parameter to the call below, so we
    // have to declare the "stats" object
    // first
    QueryStatistics stats = null;

    var list = await theSession
        .Query<Target>()
        .Stats(out stats)
        .Where(x => x.Number > 10).Take(5)
        .ToListAsync();

    list.Any().ShouldBeTrue();

    // Now, the total results data should
    // be available
    stats.TotalResults.ShouldBe(count);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Acceptance/statistics_and_paged_list.cs#L158-L184' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-query-statistics' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For the sake of completeness, the SQL generated in the operation above by Marten would be:

```sql
select d.data, d.id, count(*) OVER() as total_rows from public.mt_doc_target as d
where CAST(d.data ->> 'Number' as integer) > :arg0 LIMIT 5
```

The `Stats()` Linq operator can be used in conjunction with `Include()` and within batch queries. Compiled queries also support `QueryStatistics` by declaring a `QueryStatistics Stats { get; } = new QueryStatistics()` property on the compiled query class.

## Keyset (Cursor) Pagination

The paging shown above is **offset-based**: every page is fetched with `Skip(pageNumber * pageSize).Take(pageSize)`, typically paired with a `count(*) OVER()` window function (or a separate `count(*)` query) to compute the total row count. Offset paging is simple and lets you jump straight to an arbitrary page number, but the cost of `Skip()` grows with the offset — Postgres still has to walk and discard every skipped row — so deep pages against large tables get progressively slower, and a `count(*)` over a huge table isn't free either.

**Keyset pagination** (also called "seek" pagination) avoids that by never skipping rows at all. Instead of an offset, each page carries a small **cursor**: the sort-key values of the last row you saw. The next page is fetched with a `WHERE` clause that seeks directly to "everything after that cursor", using the same index that satisfies your `ORDER BY`. That makes each page's cost roughly constant no matter how deep you are into the result set — ideal for infinite scroll, "load more", and export/catch-up style feeds where you only ever page forward and don't need to compute a total count or jump to a specific page number.

Marten exposes keyset pagination as `ToJsonPageByCursorAsync<T>`, an extension method on `IQueryable<T>` that streams a page of raw document JSON plus the next cursor:

```csharp
// First page: pass cursor: null
var firstPage = await session.Query<Issue>()
    .OrderBy(x => x.Description).ThenBy(x => x.Id)
    .ToJsonPageByCursorAsync(cursor: null, pageSize: 25);

// firstPage.ItemsJson is a raw JSON array string: "[{...},{...},...]"
// firstPage.NextCursor is an opaque token, or null if this was the last page

// Next page: pass the previous page's cursor
var secondPage = await session.Query<Issue>()
    .OrderBy(x => x.Description).ThenBy(x => x.Id)
    .ToJsonPageByCursorAsync(firstPage.NextCursor, pageSize: 25);
```

A few rules govern how the cursor is built and validated:

- The queryable **must** have an `OrderBy`/`OrderByDescending` clause (optionally followed by `ThenBy`/`ThenByDescending` clauses) — Marten parses this ordering chain to know which column(s) to seek on. Without one, `ToJsonPageByCursorAsync` throws an `InvalidOperationException`.
- The **last** ordering in the chain must be on a member guaranteed unique across the result set — typically the document's `Id`. This tie-breaker keeps pagination deterministic when earlier sort keys have duplicate values; if it's missing, Marten throws an `InvalidOperationException` telling you which `ThenBy` to add.
- Mixed ascending/descending orderings are supported (e.g. `OrderByDescending(x => x.Date).ThenBy(x => x.Id)`) — each ordering keeps its own direction when the seek predicate is built.
- A page with fewer than `pageSize` rows means you've reached the end of the result set — `NextCursor` will be `null`.
- The cursor itself is an opaque, versioned, base64-encoded token — treat it as a black box and don't try to parse or construct one by hand.

### Streaming keyset pages directly to an HTTP response

If you're using [Marten.AspNetCore](/documents/aspnetcore), `StreamPagedByCursor<T>` wraps `ToJsonPageByCursorAsync<T>` as an `IResult` you can return straight from a Minimal API endpoint — see [StreamPagedByCursor\<T\>](/documents/aspnetcore#streampagedbycursor-t-keyset-paginated-streaming) for the full writeup, including the JSON envelope shape and the `Marten-Continuation` response header.

### When to use keyset vs. offset pagination

- Use **`ToPagedList`/`ToPagedListAsync`** (offset-based, above) when you need page numbers, jump-to-page navigation, or a total row/page count — typical of a classic paged grid UI.
- Use **`ToJsonPageByCursorAsync`/`StreamPagedByCursor<T>`** (keyset-based) when you only ever page forward, don't need a total count or arbitrary page jumps, and want consistent performance no matter how deep the pagination goes — typical of infinite scroll, "load more" buttons, and bulk export/sync feeds.
