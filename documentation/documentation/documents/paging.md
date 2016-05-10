<!--title: Paging-->

For paged access to data in Marten, just use the `Take()` and `Skip()` Linq operators. To get the total number of
documents matching a query without having to execute the query twice, use the new `Stats()` Linq operator to
also capture the total as shown in this sample:

<[sample:using-query-statistics]>

For the sake of completeness, the SQL generated in the operation above by Marten would be:

<pre>
select d.data, d.id, count(1) OVER() as total_rows from public.mt_doc_target as d where CAST(d.data ->> 'Number' as integer) > :arg0 LIMIT 5
</pre>


The `Stats()` Linq operator can be used in conjunction with `Include()` and within batch queries. Marten does not yet
support using `Stats()` within the compiled query.