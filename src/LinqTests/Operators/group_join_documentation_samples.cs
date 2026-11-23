using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;

namespace LinqTests.Operators.GroupJoinSamples;

// Compile-only samples for docs/documents/querying/linq/group-join.md. Nothing here runs as a test;
// each shape is covered by group_join_operator.cs, join_operators.cs and join_operators_are_refused.cs,
// which use the JoinCustomer / JoinOrder / JoinEmployee types.

#region sample_group_join_document_types
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
#endregion

public class group_join_documentation_samples
{
    #region sample_group_join_inner_join
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
    #endregion

    #region sample_group_join_projecting_multiple_fields
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
    #endregion

    #region sample_group_join_on_string_fields
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
    #endregion

    #region sample_group_join_filtering_before_the_join
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
    #endregion

    #region sample_group_join_left_join
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
    #endregion

    #region sample_group_join_with_duplicated_fields
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
    #endregion

    #region sample_group_join_duplicate_both_keys
    public void duplicate_both_join_keys(StoreOptions opts)
    {
        opts.Schema.For<Customer>().Duplicate(x => x.City);
        opts.Schema.For<Employee>().Duplicate(x => x.City);
    }
    #endregion

    #region sample_group_join_with_aggregation
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
    #endregion

    // Covered by Bugs/Bug_groupjoin_distinct_and_scalar_projection.cs
    #region sample_join_distinct_and_aggregates
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
    #endregion

    // Covered by Bugs/Bug_groupjoin_outer_projection_and_grouping.cs and
    // Bugs/Bug_5968_computed_ordering_keys_over_children_and_joins.cs
    #region sample_group_by_and_ordering_over_a_join
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
    #endregion

    #region sample_join_left_join_and_right_join
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
    #endregion

#if NET11_0_OR_GREATER
    #region sample_join_tuple_overloads
    public async Task join_without_a_result_selector(IQuerySession session)
    {
        // .NET 11 only: each row is an (Outer, Inner) tuple
        var names = await session.Query<Customer>()
            .Join(session.Query<Order>(), c => c.Id, o => o.CustomerId)
            .Where(x => x.Inner.Amount > 100)
            .Select(x => x.Outer.Name)
            .ToListAsync();
    }
    #endregion
#endif
}
