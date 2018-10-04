<!--title: Paging-->

For paged access to data, Marten provides `ToPagedList` and `ToPagedListAsync` extension methods on `IQueryable<T>`. 
<[sample:to_paged_list]>

<[sample:to_paged_list_async]>

If you want to create you own paged queries, just use the `Take()` and `Skip()` Linq operators in combination with `Stats()`

<[sample:using-query-statistics]>

For the sake of completeness, the SQL generated in the operation above by Marten would be:

<pre>
select d.data, d.id, count(1) OVER() as total_rows from public.mt_doc_target as d 
where CAST(d.data ->> 'Number' as integer) > :arg0 LIMIT 5
</pre>


The `Stats()` Linq operator can be used in conjunction with `Include()` and within batch queries. Marten does not yet
support using `Stats()` within the compiled query.