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

## Limitations

The following patterns are **not yet supported** and will throw `NotSupportedException`:

- **GroupJoin as a final operator** (without `SelectMany`) — materializing the grouped collection is not supported. Use `GroupJoin` + `SelectMany` instead.
- **Composite keys** — joining on multiple fields simultaneously is not supported.
- **Cross-apply / subquery joins** — only simple key-to-key "equi-joins" are supported.
