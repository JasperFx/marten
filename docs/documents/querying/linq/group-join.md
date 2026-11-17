# Joining Documents with GroupJoin

Marten supports LINQ `GroupJoin()` to perform SQL `JOIN` operations between different document types stored in PostgreSQL. This lets you combine data from two document collections based on a matching key, similar to SQL `INNER JOIN` and `LEFT JOIN`.

::: tip
Marten translates `GroupJoin()` into CTE-based SQL JOINs, where each document table is queried independently in a Common Table Expression (CTE) and the results are joined. This approach works naturally with Marten's JSONB document storage.
:::

## Inner Join (GroupJoin + SelectMany)

The most common pattern uses `GroupJoin()` followed by `SelectMany()` to produce a flattened inner join. Only rows with matching keys on both sides are included in the result.

```cs
// Document types
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
```

### Basic Inner Join

```cs
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
```

This generates SQL equivalent to:

```sql
WITH outer_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_customer AS d
),
inner_cte AS (
    SELECT d.id, d.data FROM public.mt_doc_order AS d
)
SELECT jsonb_build_object('CustomerName', outer_cte.data ->> 'Name', 'OrderAmount', CAST(inner_cte.data ->> 'Amount' AS numeric)) AS data
FROM outer_cte
INNER JOIN inner_cte ON CAST(outer_cte.data ->> 'Id' AS uuid) = CAST(inner_cte.data ->> 'CustomerId' AS uuid);
```

### Projecting Multiple Fields

You can project any combination of fields from both the outer and inner documents:

```cs
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
```

### Joining on String Fields

Join keys are not limited to Guid/Id fields. You can join on any field type:

```cs
public class Employee
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
}

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
```

## Filtering Before the Join

`Where` clauses on either side of the join are pushed into the corresponding CTE so filtering happens **before** the join. This applies both to a `Where` applied to the outer source and to a `Where` chained onto the inner `IQueryable` passed as the second argument to `GroupJoin`.

```cs
var results = await session.Query<Customer>()
    .Where(c => c.City == "Seattle")                        // filters outer CTE
    .GroupJoin(
        session.Query<Order>().Where(o => o.Status == "Shipped"), // filters inner CTE
        c => c.Id,
        o => o.CustomerId,
        (c, orders) => new { c, orders })
    .SelectMany(
        x => x.orders,
        (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
    .ToListAsync();
```

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

## Left Join (GroupJoin + SelectMany + DefaultIfEmpty)

To include outer rows that have no matching inner rows (a SQL `LEFT JOIN`), add `.DefaultIfEmpty()` to the collection selector in `SelectMany()`:

```cs
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

// Customers with no orders will appear with null values for order fields
```

::: info
When using `DefaultIfEmpty()` for left joins, inner-side projected fields should use nullable types (e.g., `decimal?` instead of `decimal`) since they will be `null` for unmatched rows.
:::

## With Duplicated Fields

GroupJoin works naturally with Marten's [duplicated fields](/documents/indexing/duplicated-fields). When a join key or projected field is configured as duplicated, Marten uses the physical column in the CTE instead of JSONB extraction, which can improve join performance:

```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    // Duplicate the join key for better performance
    opts.Schema.For<Order>().Duplicate(x => x.CustomerId);
});

// The join query is identical — Marten automatically uses the duplicated column
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
```

When both sides of the join key are duplicated, the ON clause uses direct column comparisons rather than JSONB extraction, which is significantly faster for large document collections:

```cs
opts.Schema.For<Customer>().Duplicate(x => x.City);
opts.Schema.For<Employee>().Duplicate(x => x.City);
```

## With Aggregation

Standard LINQ aggregation operators work after a GroupJoin:

```cs
// Count joined results
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

// First joined result
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
```

## Join, LeftJoin, RightJoin and FullJoin

Marten translates `Queryable.Join()` and the `LeftJoin()` and `RightJoin()` operators added in .NET 10 by rewriting
each one into the `GroupJoin()` + `SelectMany()` form above before the query is parsed. The SQL is the same as for
the hand-written form, and so is everything you can apply after the join: `Where()`, `Select()`, `OrderBy()`,
`Take()`, `Skip()`, `Count()`, `Any()` and `First()`.

| Operator | Translated as |
| --- | --- |
| `Join()` | `GroupJoin()` + `SelectMany()`, a SQL `INNER JOIN` |
| `LeftJoin()` | `GroupJoin()` + `SelectMany()` over `DefaultIfEmpty()`, a SQL `LEFT JOIN` |
| `RightJoin()` | A left join with the two sources and key selectors swapped |
| `FullJoin()` | Not translated: Marten has no `FULL OUTER JOIN`. Query each side and combine in memory, or use `session.QueryAsync<T>(sql)` |

```cs
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
```

.NET 11 also adds overloads of `Join()`, `LeftJoin()` and `RightJoin()` without a result selector, which return
`(Outer, Inner)` tuples. Those translate too, and the tuple's members can be filtered, ordered and projected after
the join:

```cs
var names = await session.Query<Customer>()
    .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId)
    .Where(x => x.Inner.Amount > 100)
    .Select(x => x.Outer.Name)
    .ToListAsync();
```

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

## Limitations

The following patterns are **not yet supported** and will throw `NotSupportedException`:

- **GroupJoin as a final operator** (without `SelectMany`) — materializing the grouped collection is not supported. Use `GroupJoin` + `SelectMany` instead.
- **Composite keys** — joining on multiple fields simultaneously is not supported.
- **A `Where()` on the inner side after a left join** (`DefaultIfEmpty()`) throws `BadLinqExpressionException`; see above.
- **Cross-apply / subquery joins** — only simple key-to-key joins are supported.
