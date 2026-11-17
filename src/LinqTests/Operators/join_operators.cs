using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public record JoinedRow(string Customer, string Status, decimal? Amount);

// #5910: Queryable.Join, LeftJoin and RightJoin are rewritten into the GroupJoin() + SelectMany()
// (+ DefaultIfEmpty()) form before the query is parsed, so each one is checked twice: the rows match
// LINQ to Objects over the same data, and the SQL is exactly what the hand-written GroupJoin form gets.
public class join_operators: OneOffConfigurationsContext
{
    private IDocumentStore _store = null!;
    private IDocumentSession _session = null!;

    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _charlie = Guid.NewGuid();

    private JoinCustomer[] _customers = null!;
    private JoinOrder[] _orders = null!;

    private async Task SetupData()
    {
        _store = StoreOptions(opts =>
        {
            opts.Schema.For<JoinCustomer>();
            opts.Schema.For<JoinOrder>();
        });

        _session = _store.LightweightSession();
        _disposables.Add(_session);

        _customers =
        [
            new JoinCustomer { Id = _alice, Name = "Alice", City = "Seattle" },
            new JoinCustomer { Id = _bob, Name = "Bob", City = "Portland" },
            // Charlie has no orders
            new JoinCustomer { Id = _charlie, Name = "Charlie", City = "Seattle" }
        ];

        _orders =
        [
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _alice, Status = "Active", Amount = 100m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _alice, Status = "Shipped", Amount = 200m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _bob, Status = "Active", Amount = 300m },
            // An order whose customer does not exist, for the right join
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = "Orphaned", Amount = 400m }
        ];

        _session.Store(_customers);
        _session.Store(_orders);
        await _session.SaveChangesAsync();
    }

    private static List<JoinedRow> sorted(IEnumerable<JoinedRow> rows)
    {
        return rows.OrderBy(x => x.Customer).ThenBy(x => x.Status).ThenBy(x => x.Amount).ToList();
    }

    // Runs each query on a fresh session -- so the CTE names, numbered per session, line up -- and
    // compares the SQL and the parameters Marten actually sent. That ToCommand() previews the same SQL
    // is pinned separately, by preview_command_matches_the_executed_command (#5916).
    private async Task executedSqlShouldMatch(Func<IQuerySession, Task> rewritten, Func<IQuerySession, Task> handWritten)
    {
        var actual = await executedSql(rewritten);
        var expected = await executedSql(handWritten);

        actual.Sql.ShouldBe(expected.Sql);
        actual.Parameters.ShouldBe(expected.Parameters);
    }

    private async Task<(string Sql, object[] Parameters)> executedSql(Func<IQuerySession, Task> query)
    {
        await using var session = _store.QuerySession();
        var logger = new ExecutedCommandRecorder();
        session.Logger = logger;

        await query(session);

        logger.Commands.ShouldNotBeEmpty();
        return (string.Join(Environment.NewLine, logger.Commands.Select(x => x.Sql)),
            logger.Commands.SelectMany(x => x.Parameters).ToArray());
    }

    #region Join

    [Fact]
    public async Task join_returns_the_matching_rows()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .ToListAsync();

        var expected = _customers.Join(_orders, c => c.Id, o => o.CustomerId,
            (c, o) => new JoinedRow(c.Name, o.Status, o.Amount));

        sorted(results).ShouldBe(sorted(expected));
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task join_sql_is_the_group_join_sql()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                    (c, o) => new { c.Name, o.Amount })
                .ToListAsync(),
            s => s.Query<JoinCustomer>()
                .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
                .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Amount })
                .ToListAsync());
    }

    [Fact]
    public async Task join_projecting_both_documents()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { Customer = c, Order = o })
            .ToListAsync();

        results.Select(x => (x.Customer.Id, x.Order.Id)).OrderBy(x => x.Item2)
            .ShouldBe(_customers.Join(_orders, c => c.Id, o => o.CustomerId, (c, o) => (c.Id, o.Id))
                .OrderBy(x => x.Item2));
    }

    [Fact]
    public async Task join_returning_one_whole_side()
    {
        await SetupData();

        var orders = await _session.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => o)
            .ToListAsync();

        orders.Select(x => x.Amount).OrderBy(x => x).ShouldBe([100m, 200m]);
    }

    [Fact]
    public async Task join_then_select()
    {
        await SetupData();

        var amounts = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount })
            .Select(x => x.Amount)
            .ToListAsync();

        amounts.OrderBy(x => x).ShouldBe([100m, 200m, 300m]);
    }

    [Fact]
    public async Task join_with_where_before_and_after_and_on_the_inner_source()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .Join(_session.Query<JoinOrder>().Where(o => o.Status != "Cancelled"), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .Where(x => x.Amount > 150m)
            .ToListAsync();

        var expected = _customers.Where(c => c.City == "Seattle")
            .Join(_orders.Where(o => o.Status != "Cancelled"), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .Where(x => x.Amount > 150m);

        sorted(results).ShouldBe(sorted(expected));
        results.Single().Amount.ShouldBe(200m);
    }

    [Fact]
    public async Task join_with_where_sql_is_the_group_join_sql()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .Where(c => c.City == "Seattle")
                .Join(s.Query<JoinOrder>().Where(o => o.Status == "Active"), c => c.Id, o => o.CustomerId,
                    (c, o) => new { c.Name, o.Amount })
                .Where(x => x.Amount > 150m)
                .OrderBy(x => x.Amount)
                .Take(5)
                .ToListAsync(),
            s => s.Query<JoinCustomer>()
                .Where(c => c.City == "Seattle")
                .GroupJoin(s.Query<JoinOrder>().Where(o => o.Status == "Active"), c => c.Id, o => o.CustomerId,
                    (c, orders) => new { c, orders })
                .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Amount })
                .Where(x => x.Amount > 150m)
                .OrderBy(x => x.Amount)
                .Take(5)
                .ToListAsync());
    }

    [Fact]
    public async Task join_where_on_the_outer_side_after_the_join()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .Where(x => x.Customer == "Alice")
            .ToListAsync();

        results.Select(x => x.Amount).OrderBy(x => x).ShouldBe([100m, 200m]);
    }

    [Fact]
    public async Task join_ordered_and_paged_after_the_join()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .OrderByDescending(x => x.Amount)
            .Take(2)
            .ToListAsync();

        results.Select(x => x.Amount).ShouldBe([300m, 200m]);
        results.Select(x => x.Customer).ShouldBe(["Bob", "Alice"]);
    }

    [Fact]
    public async Task count_and_any_over_a_join()
    {
        await SetupData();

        var joined = _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount });

        (await joined.CountAsync()).ShouldBe(3);
        (await joined.Where(x => x.Amount > 250m).CountAsync()).ShouldBe(1);
        (await joined.AnyAsync()).ShouldBeTrue();
        (await joined.Where(x => x.Amount > 1000m).AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task count_and_any_over_a_join_sql_is_the_group_join_sql()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount })
                .CountAsync(),
            s => s.Query<JoinCustomer>()
                .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
                .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Amount })
                .CountAsync());

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount })
                .Where(x => x.Amount > 1000m)
                .AnyAsync(),
            s => s.Query<JoinCustomer>()
                .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
                .SelectMany(x => x.orders, (x, o) => new { x.c.Name, o.Amount })
                .Where(x => x.Amount > 1000m)
                .AnyAsync());
    }

    [Fact]
    public async Task join_on_a_string_key()
    {
        await SetupData();

        // Customers in the same city, a self join on a non-id member
        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinCustomer>(), a => a.City, b => b.City,
                (a, b) => new { First = a.Name, Second = b.Name })
            .ToListAsync();

        results.Select(x => $"{x.First}-{x.Second}").OrderBy(x => x).ShouldBe(
            _customers.Join(_customers, a => a.City, b => b.City, (a, b) => $"{a.Name}-{b.Name}").OrderBy(x => x));
    }

    #endregion

    #region LeftJoin

    [Fact]
    public async Task left_join_keeps_an_unmatched_outer_with_a_null_inner()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .LeftJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, (decimal?)o.Amount))
            .ToListAsync();

        var expected = _customers.LeftJoin(_orders, c => c.Id, o => o.CustomerId,
            (c, o) => new JoinedRow(c.Name, o?.Status, o?.Amount));

        sorted(results).ShouldBe(sorted(expected));

        var charlie = results.Single(x => x.Customer == "Charlie");
        charlie.Status.ShouldBeNull();
        charlie.Amount.ShouldBeNull();
    }

    [Fact]
    public async Task left_join_sql_is_the_group_join_default_if_empty_sql()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .LeftJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                    (c, o) => new { c.Name, Amount = (decimal?)o.Amount })
                .ToListAsync(),
            s => s.Query<JoinCustomer>()
                .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
                .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Amount = (decimal?)o.Amount })
                .ToListAsync());
    }

    [Fact]
    public async Task left_join_with_where_before_and_ordering_after()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .LeftJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, (decimal?)o.Amount))
            .OrderBy(x => x.Customer)
            .ToListAsync();

        results.Select(x => x.Customer).ShouldBe(["Alice", "Alice", "Charlie"]);
        results.Last().Amount.ShouldBeNull();
    }

    [Fact]
    public async Task left_join_then_a_where_on_the_outer_side()
    {
        await SetupData();

        // A filter on the kept side means the same before and after the join
        var results = await _session.Query<JoinCustomer>()
            .LeftJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, (decimal?)o.Amount))
            .Where(x => x.Customer == "Charlie")
            .ToListAsync();

        results.ShouldBe([new JoinedRow("Charlie", null, null)]);
    }

    [Fact]
    public async Task count_over_a_left_join()
    {
        await SetupData();

        var count = await _session.Query<JoinCustomer>()
            .LeftJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, Amount = (decimal?)o.Amount })
            .CountAsync();

        count.ShouldBe(4);
    }

    #endregion

    #region RightJoin

    [Fact]
    public async Task right_join_keeps_an_unmatched_inner_with_a_null_outer()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .RightJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .ToListAsync();

        var expected = _customers.RightJoin(_orders, c => c.Id, o => o.CustomerId,
            (c, o) => new JoinedRow(c?.Name, o.Status, o.Amount));

        sorted(results).ShouldBe(sorted(expected));

        var orphan = results.Single(x => x.Status == "Orphaned");
        orphan.Customer.ShouldBeNull();
        orphan.Amount.ShouldBe(400m);
        results.ShouldNotContain(x => x.Customer == "Charlie");
    }

    [Fact]
    public async Task right_join_sql_is_the_swapped_left_join_sql()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .RightJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                    (c, o) => new { c.Name, o.Amount })
                .ToListAsync(),
            s => s.Query<JoinOrder>()
                .GroupJoin(s.Query<JoinCustomer>(), o => o.CustomerId, c => c.Id, (o, customers) => new { o, customers })
                .SelectMany(x => x.customers.DefaultIfEmpty(), (x, c) => new { c.Name, x.o.Amount })
                .ToListAsync());
    }

    [Fact]
    public async Task right_join_filters_both_sources()
    {
        await SetupData();

        // The Where on the customers keeps every order, and blanks the customer of the ones it removes
        var results = await _session.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .RightJoin(_session.Query<JoinOrder>().Where(o => o.Status == "Active"), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .ToListAsync();

        var expected = _customers.Where(c => c.City == "Seattle")
            .RightJoin(_orders.Where(o => o.Status == "Active"), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c?.Name, o.Status, o.Amount));

        sorted(results).ShouldBe(sorted(expected));
        results.Single(x => x.Amount == 300m).Customer.ShouldBeNull();
    }

    #endregion
#if NET11_0_OR_GREATER
    #region .NET 11 tuple overloads

    [Fact]
    public async Task tuple_join_returns_both_documents()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId)
            .ToListAsync();

        results.Select(x => (x.Outer.Name, x.Inner.Amount)).OrderBy(x => x.Amount)
            .ShouldBe([("Alice", 100m), ("Alice", 200m), ("Bob", 300m)]);
    }

    [Fact]
    public async Task tuple_join_sql_is_the_group_join_sql_with_a_tuple_result_selector()
    {
        await SetupData();

        await executedSqlShouldMatch(
            s => s.Query<JoinCustomer>()
                .Join(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId)
                .ToListAsync(),
            s => s.Query<JoinCustomer>()
                .GroupJoin(s.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
                .SelectMany(x => x.orders, (x, o) => new ValueTuple<JoinCustomer, JoinOrder>(x.c, o))
                .ToListAsync());
    }

    [Fact]
    public async Task tuple_left_join_has_a_null_inner_for_an_unmatched_outer()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .LeftJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId)
            .ToListAsync();

        results.Count.ShouldBe(4);
        results.Single(x => x.Outer.Name == "Charlie").Inner.ShouldBeNull();
        results.Where(x => x.Inner != null).Select(x => x.Inner!.Amount).OrderBy(x => x).ShouldBe([100m, 200m, 300m]);
    }

    [Fact]
    public async Task tuple_right_join_has_a_null_outer_for_an_unmatched_inner()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .RightJoin(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId)
            .ToListAsync();

        results.Count.ShouldBe(4);
        results.Single(x => x.Inner.Status == "Orphaned").Outer.ShouldBeNull();
        results.Where(x => x.Outer != null).Select(x => x.Outer!.Name).OrderBy(x => x)
            .ShouldBe(["Alice", "Alice", "Bob"]);
    }

    [Fact]
    public async Task tuple_join_then_filtered_and_projected()
    {
        await SetupData();

        var names = await _session.Query<JoinCustomer>()
            .Join(_session.Query<JoinOrder>(), c => c.Id, o => o.CustomerId)
            .Where(x => x.Inner.Amount > 150m)
            .OrderByDescending(x => x.Inner.Amount)
            .Select(x => x.Outer.Name)
            .ToListAsync();

        names.ShouldBe(["Bob", "Alice"]);
    }

    #endregion
#endif
}
