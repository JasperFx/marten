# Joining Documents with GroupJoin

Marten supports LINQ `GroupJoin()` to perform SQL `JOIN` operations between different document types stored in PostgreSQL. This lets you combine data from two document collections based on a matching key, similar to SQL `INNER JOIN` and `LEFT JOIN`.

::: tip
Marten translates `GroupJoin()` into CTE-based SQL JOINs, where each document table is queried independently in a Common Table Expression (CTE) and the results are joined. This approach works naturally with Marten's JSONB document storage.
:::

## Inner Join (GroupJoin + SelectMany) {#inner-join}

The most common pattern uses `GroupJoin()` followed by `SelectMany()` to produce a flattened inner join. Only rows with matching keys on both sides are included in the result.

The samples on this page use these document types:

<!-- snippet: sample_group_join_document_types -->
<a id='snippet-sample_group_join_document_types'></a>
```cs
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
}

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; }
    public decimal Amount { get; set; }
}

public class Employee
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L12-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_document_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Basic Inner Join

<!-- snippet: sample_group_join_inner_join -->
<a id='snippet-sample_group_join_inner_join'></a>
```cs
public async Task inner_join(IQuerySession session)
{
    var results = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L38-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_inner_join' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This generates SQL roughly like the following. Marten names the CTEs itself (`mt_temp_id_list1CTE` and so on), shown
here as `outer_cte` and `inner_cte`. A key on `Id` uses the `id` column:

```sql
WITH outer_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_customer AS d
),
inner_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_order AS d
)
SELECT jsonb_build_object('CustomerName', outer_cte.data ->> 'Name', 'OrderAmount', ...) AS data
FROM outer_cte
INNER JOIN inner_cte ON outer_cte.id = CAST(inner_cte.data ->> 'CustomerId' AS uuid);
```

### Projecting Multiple Fields

You can project any combination of fields from both the outer and inner documents:

<!-- snippet: sample_group_join_projecting_multiple_fields -->
<a id='snippet-sample_group_join_projecting_multiple_fields'></a>
```cs
public async Task projecting_multiple_fields(IQuerySession session)
{
    var results = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { Customer = x.c.Name, Order = o.Status, o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L54-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_projecting_multiple_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Joining on String Fields

Join keys are not limited to Guid/Id fields. You can join on any field type:

<!-- snippet: sample_group_join_on_string_fields -->
<a id='snippet-sample_group_join_on_string_fields'></a>
```cs
public async Task joining_on_string_fields(IQuerySession session)
{
    // Join customers and employees by city
    var results = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Employee>(),
            c => c.City,
            e => e.City,
            (c, employees) => new { c, employees })
        .SelectMany(
            x => x.employees,
            (x, e) => new { Customer = x.c.Name, Employee = e.Name, x.c.City })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L70-L85' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_on_string_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Filtering Before the Join

`Where` clauses on either side of the join are pushed into the corresponding CTE so filtering happens **before** the join. This applies both to a `Where` applied to the outer source and to a `Where` chained onto the inner `IQueryable` passed as the second argument to `GroupJoin`.

<!-- snippet: sample_group_join_filtering_before_the_join -->
<a id='snippet-sample_group_join_filtering_before_the_join'></a>
```cs
public async Task filtering_before_the_join(IQuerySession session)
{
    var results = await session.Query<Customer>()
        // Filters the outer CTE
        .Where(c => c.City == "Seattle")
        .GroupJoin(
            // Filters the inner CTE
            session.Query<Order>().Where(o => o.Status == "Shipped"),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L87-L104' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_filtering_before_the_join' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The generated SQL applies each `Where` to its own CTE so the join only sees pre-filtered rows from both sides:

```sql
WITH outer_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_customer AS d
    WHERE d.data ->> 'City' = 'Seattle'
),
inner_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_order AS d
    WHERE d.data ->> 'Status' = 'Shipped'
)
SELECT ... FROM outer_cte INNER JOIN inner_cte ON ...;
```

Multiple `Where` calls on either source are supported and are AND-ed together.

## Left Join (GroupJoin + SelectMany + DefaultIfEmpty) {#left-join}

To include outer rows that have no matching inner rows (a SQL `LEFT JOIN`), add `.DefaultIfEmpty()` to the collection selector in `SelectMany()`:

<!-- snippet: sample_group_join_left_join -->
<a id='snippet-sample_group_join_left_join'></a>
```cs
public async Task left_join(IQuerySession session)
{
    // Customers with no orders appear with a null OrderAmount
    var results = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders.DefaultIfEmpty(),
            (x, o) => new { CustomerName = x.c.Name, OrderAmount = (decimal?)o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L106-L121' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_left_join' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: info
When using `DefaultIfEmpty()` for left joins, inner-side projected fields should use nullable types (e.g., `decimal?` instead of `decimal`) since they will be `null` for unmatched rows.
:::

## With Duplicated Fields

GroupJoin works naturally with Marten's [duplicated fields](/documents/indexing/duplicated-fields). When a join key or projected field is configured as duplicated, Marten uses the physical column in the CTE instead of JSONB extraction, which can improve join performance:

<!-- snippet: sample_group_join_with_duplicated_fields -->
<a id='snippet-sample_group_join_with_duplicated_fields'></a>
```cs
public async Task with_duplicated_fields(string connectionString)
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(connectionString);

        // Duplicate the join key for better performance
        opts.Schema.For<Order>().Duplicate(x => x.CustomerId);
    });

    await using var session = store.QuerySession();

    // The join query is identical. Marten uses the duplicated column
    var results = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L123-L148' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_with_duplicated_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When both sides of the join key are duplicated, the ON clause uses direct column comparisons rather than JSONB extraction, which is significantly faster for large document collections:

<!-- snippet: sample_group_join_duplicate_both_keys -->
<a id='snippet-sample_group_join_duplicate_both_keys'></a>
```cs
public void duplicate_both_join_keys(StoreOptions opts)
{
    opts.Schema.For<Customer>().Duplicate(x => x.City);
    opts.Schema.For<Employee>().Duplicate(x => x.City);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L150-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_duplicate_both_keys' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## With Aggregation {#with-aggregation}

Standard LINQ aggregation operators work after a GroupJoin:

<!-- snippet: sample_group_join_with_aggregation -->
<a id='snippet-sample_group_join_with_aggregation'></a>
```cs
public async Task with_aggregation(IQuerySession session)
{
    // Count the joined rows
    var count = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { CustomerName = x.c.Name, o.Amount })
        .CountAsync();

    // The first joined row
    var first = await session.Query<Customer>()
        .GroupJoin(
            session.Query<Order>(),
            c => c.Id,
            o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(
            x => x.orders,
            (x, o) => new { CustomerName = x.c.Name, o.Amount })
        .FirstAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L158-L185' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_join_with_aggregation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Distinct()` de-duplicates the joined rows, and `SumAsync()`, `MinAsync()`, `MaxAsync()`, `AverageAsync()` and
`CountAsync()` work over a scalar projection of a join. This sample uses `Join()`, which is the same join in one
operator, described [below](#join-leftjoin-rightjoin-and-fulljoin):

<!-- snippet: sample_join_distinct_and_aggregates -->
<a id='snippet-sample_join_distinct_and_aggregates'></a>
```cs
public async Task join_distinct_and_aggregates(IQuerySession session)
{
    // Distinct() de-duplicates the joined rows
    var pairs = await session.Query<Customer>()
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { c.Name, o.Status })
        .Distinct()
        .ToListAsync();

    // Aggregates over a scalar projection of the join
    var amounts = session.Query<Customer>()
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId, (c, o) => o.Amount);

    var total = await amounts.SumAsync(x => x);
    var largest = await amounts.MaxAsync(x => x);
    var distinctAmounts = await amounts.Distinct().CountAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L188-L206' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_join_distinct_and_aggregates' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Join, LeftJoin, RightJoin and FullJoin {#join-leftjoin-rightjoin-and-fulljoin}

Marten translates `Queryable.Join()` and the `LeftJoin()` and `RightJoin()` operators added in .NET 10 by rewriting
each one into the `GroupJoin()` + `SelectMany()` form above before the query is parsed. The SQL is the same as for
the hand-written form, and so is everything you can apply after the join: `Where()`, `Select()`, `OrderBy()`,
`Take()`, `Skip()`, `CountAsync()`, `AnyAsync()` and `FirstAsync()`.

| Operator | Translated as |
| --- | --- |
| `Join()` | `GroupJoin()` + `SelectMany()`, a SQL `INNER JOIN` |
| `LeftJoin()` | `GroupJoin()` + `SelectMany()` over `DefaultIfEmpty()`, a SQL `LEFT JOIN` |
| `RightJoin()` | A left join with the two sources and key selectors swapped |
| `FullJoin()` (.NET 11) | Not translated: Marten has no `FULL OUTER JOIN`. Query each side and combine in memory, or use `session.QueryAsync<T>(sql)` |

<!-- snippet: sample_join_left_join_and_right_join -->
<a id='snippet-sample_join_left_join_and_right_join'></a>
```cs
public async Task join_left_join_and_right_join(IQuerySession session)
{
    // Inner join
    var orders = await session.Query<Customer>()
        .Where(c => c.City == "Seattle")
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { c.Name, o.Amount })
        .OrderByDescending(x => x.Amount)
        .ToListAsync();

    // Left join: a customer with no orders appears once, with a null order
    var everyone = await session.Query<Customer>()
        .LeftJoin(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { c.Name, Amount = (decimal?)o.Amount })
        .ToListAsync();

    // Right join: an order whose customer does not exist appears with a null customer
    var allOrders = await session.Query<Customer>()
        .RightJoin(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { Customer = c.Name, o.Amount })
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L238-L261' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_join_left_join_and_right_join' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

.NET 11 also adds overloads of `Join()`, `LeftJoin()` and `RightJoin()` without a result selector, which return
`(Outer, Inner)` tuples. Those translate too, and the tuple's members can be filtered, ordered and projected after
the join:

<!-- snippet: sample_join_tuple_overloads -->
<a id='snippet-sample_join_tuple_overloads'></a>
```cs
public async Task join_without_a_result_selector(IQuerySession session)
{
    // .NET 11 only: each row is an (Outer, Inner) tuple
    var names = await session.Query<Customer>()
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId)
        .Where(x => x.Inner.Amount > 100)
        .Select(x => x.Outer.Name)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L264-L274' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_join_tuple_overloads' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Only the shapes that rewrite answers correctly are translated. Marten refuses the rest with
`BadLinqExpressionException` rather than returning rows that look right and are not:

- **An operator other than `Where()` before the join** on either source, such as `Take()`, `Skip()`, `OrderBy()`,
  `Select()` or `Distinct()`. A SQL join does not keep the order of its inputs, and a page taken before the join is a
  different set of rows from one taken after it. Apply these after the join.
- **An inner source that is not `session.Query<T>()`**, optionally filtered by `Where()`. That rules out joining to an
  in-memory collection, and joins inside a compiled query, whose only source is its own parameter.
- **A composite or computed key.** Each key selector must be a single member of the document, such as
  `o => o.CustomerId`.
- **An `IEqualityComparer` overload.** SQL compares the keys with `=`.
- **A join whose source is itself a join.** Marten translates one join per query.
- **A `Where()` on the nullable side after a left join** (the inner side of `LeftJoin()` or of `DefaultIfEmpty()`,
  the outer side of `RightJoin()`). Marten would apply that filter to the inner rows before joining, which keeps the
  rows the filter should remove. Filter the inner source before the join if that is what you mean, or use an inner
  join for the rows that have a match.

## Grouping and Ordering over a Join {#grouping-and-ordering-over-a-join}

`GroupBy()` after a join groups the joined rows, with the same aggregates as
[GroupBy()](/documents/querying/linq/operators#groupby). The result selector of the `SelectMany()` after a `GroupJoin()` can
return the whole outer document, `(x, _) => x.c`, which gives one row per match. An `OrderBy()` key over the joined rows can be computed,
with the same translations as [Computed Ordering Keys](/documents/querying/linq/operators#computed-ordering-keys):

<!-- snippet: sample_group_by_and_ordering_over_a_join -->
<a id='snippet-sample_group_by_and_ordering_over_a_join'></a>
```cs
public async Task group_by_and_ordering_over_a_join(IQuerySession session)
{
    // Group the joined rows
    var totals = await session.Query<Customer>()
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { c.Name, o.Amount })
        .GroupBy(x => x.Name)
        .Select(g => new { Name = g.Key, Orders = g.Count(), Total = g.Sum(x => x.Amount) })
        .ToListAsync();

    // Project the whole outer document: one customer row per matching order
    var customers = await session.Query<Customer>()
        .GroupJoin(session.Query<Order>().Where(o => o.Amount > 100), c => c.Id, o => o.CustomerId,
            (c, orders) => new { c, orders })
        .SelectMany(x => x.orders, (x, _) => x.c)
        .Where(c => c.City == "Seattle")
        .ToListAsync();

    // A computed key over either side of the join
    var largestFirst = await session.Query<Customer>()
        .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId,
            (c, o) => new { c.Name, o.Amount })
        .OrderBy(x => -x.Amount)
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Operators/group_join_documentation_samples.cs#L210-L236' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_group_by_and_ordering_over_a_join' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Limitations {#limitations}

- **GroupJoin as the final operator** (no `SelectMany`) throws `NotSupportedException`. Use `GroupJoin` + `SelectMany`.
- **Composite keys** and **cross-apply or subquery joins** throw `BadLinqExpressionException`; see the rules above.
