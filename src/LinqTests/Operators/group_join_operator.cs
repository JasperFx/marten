using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class JoinCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

public class JoinOrder
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}

public class JoinEmployee
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

public class group_join_operator: OneOffConfigurationsContext
{
    private IDocumentStore _store;
    private IDocumentSession _session;

    private Guid _customer1Id;
    private Guid _customer2Id;
    private Guid _customer3Id;

    private async Task SetupData()
    {
        _store = StoreOptions(opts =>
        {
            opts.Schema.For<JoinCustomer>();
            opts.Schema.For<JoinOrder>();
            opts.Schema.For<JoinEmployee>();
        });

        _session = _store.LightweightSession();
        _disposables.Add(_session);

        _customer1Id = Guid.NewGuid();
        _customer2Id = Guid.NewGuid();
        _customer3Id = Guid.NewGuid();

        var customers = new[]
        {
            new JoinCustomer { Id = _customer1Id, Name = "Alice", City = "Seattle" },
            new JoinCustomer { Id = _customer2Id, Name = "Bob", City = "Portland" },
            new JoinCustomer { Id = _customer3Id, Name = "Charlie", City = "Seattle" }
        };

        var orders = new[]
        {
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _customer1Id, Status = "Active", Amount = 100m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _customer1Id, Status = "Shipped", Amount = 200m },
            new JoinOrder { Id = Guid.NewGuid(), CustomerId = _customer2Id, Status = "Active", Amount = 300m },
            // No orders for Charlie (_customer3Id)
        };

        var employees = new[]
        {
            new JoinEmployee { Id = Guid.NewGuid(), Name = "Eve", City = "Seattle" },
            new JoinEmployee { Id = Guid.NewGuid(), Name = "Frank", City = "Portland" },
            new JoinEmployee { Id = Guid.NewGuid(), Name = "Grace", City = "Denver" }
        };

        _session.Store(customers);
        _session.Store(orders);
        _session.Store(employees);
        await _session.SaveChangesAsync();
    }

    #region Inner Join Tests (GroupJoin + SelectMany)

    [Fact]
    public async Task GroupJoin_simple_inner_join()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Count(r => r.CustomerName == "Alice").ShouldBe(2);
        results.Count(r => r.CustomerName == "Bob").ShouldBe(1);
        // Charlie has no orders, should not appear in inner join
        results.ShouldNotContain(r => r.CustomerName == "Charlie");
    }

    [Fact]
    public async Task GroupJoin_select_outer_entity_properties()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { x.c.Name, x.c.City })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Count(r => r.City == "Seattle").ShouldBe(2); // Alice's 2 orders
        results.Count(r => r.City == "Portland").ShouldBe(1); // Bob's 1 order
    }

    [Fact]
    public async Task GroupJoin_project_to_anonymous_type()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { Customer = x.c.Name, Order = o.Status, o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        var aliceActive = results.FirstOrDefault(r => r.Customer == "Alice" && r.Order == "Active");
        aliceActive.ShouldNotBeNull();
        aliceActive.Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task GroupJoin_on_string_field()
    {
        await SetupData();

        // Join customers and employees by City (string field)
        var results = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinEmployee>(),
                c => c.City,
                e => e.City,
                (c, employees) => new { c, employees })
            .SelectMany(
                x => x.employees,
                (x, e) => new { Customer = x.c.Name, Employee = e.Name, x.c.City })
            .ToListAsync();

        // Seattle: Alice+Eve, Alice+Charlie doesn't exist, Charlie+Eve
        // Portland: Bob+Frank
        results.Count(r => r.City == "Seattle").ShouldBe(2); // Alice+Eve, Charlie+Eve
        results.Count(r => r.City == "Portland").ShouldBe(1); // Bob+Frank
        results.ShouldNotContain(r => r.City == "Denver"); // Grace has no matching customers
    }

    #endregion

    #region Left Join Tests (GroupJoin + SelectMany + DefaultIfEmpty)

    [Fact]
    public async Task GroupJoin_DefaultIfEmpty_left_join()
    {
        await SetupData();

        var results = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders.DefaultIfEmpty(),
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = (decimal?)o.Amount })
            .ToListAsync();

        // All customers should appear, including Charlie with no orders
        results.Count.ShouldBe(4); // Alice(2) + Bob(1) + Charlie(1 null)
        results.Count(r => r.CustomerName == "Alice").ShouldBe(2);
        results.Count(r => r.CustomerName == "Bob").ShouldBe(1);
        results.Count(r => r.CustomerName == "Charlie").ShouldBe(1);
    }

    #endregion

    #region Unsupported Pattern Tests

    [Fact]
    public async Task GroupJoin_as_final_operator_should_throw()
    {
        await SetupData();

        await Should.ThrowAsync<NotSupportedException>(async () =>
        {
            // GroupJoin without SelectMany (Pattern 3 - not supported)
            await _session.Query<JoinCustomer>()
                .GroupJoin(
                    _session.Query<JoinOrder>(),
                    c => c.Id,
                    o => o.CustomerId,
                    (c, orders) => new { c, orders = orders.ToList() })
                .ToListAsync();
        });
    }

    #endregion

    #region Duplicated Field Join Tests

    private async Task<IDocumentSession> SetupWithDuplicatedInnerKey()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<JoinCustomer>();
            opts.Schema.For<JoinOrder>().Duplicate(x => x.CustomerId);
            opts.Schema.For<JoinEmployee>();
        });

        var session = store.LightweightSession();
        _disposables.Add(session);

        var customer1Id = Guid.NewGuid();
        var customer2Id = Guid.NewGuid();
        var customer3Id = Guid.NewGuid();

        session.Store(new JoinCustomer { Id = customer1Id, Name = "Alice", City = "Seattle" });
        session.Store(new JoinCustomer { Id = customer2Id, Name = "Bob", City = "Portland" });
        session.Store(new JoinCustomer { Id = customer3Id, Name = "Charlie", City = "Seattle" });

        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer1Id, Status = "Active", Amount = 100m });
        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer1Id, Status = "Shipped", Amount = 200m });
        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer2Id, Status = "Active", Amount = 300m });

        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Eve", City = "Seattle" });
        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Frank", City = "Portland" });

        await session.SaveChangesAsync();
        return session;
    }

    private async Task<IDocumentSession> SetupWithDuplicatedOuterKey()
    {
        var store = StoreOptions(opts =>
        {
            // Duplicate the Id on customer (the outer key for join)
            // Note: Id is the identity field, but we can also duplicate other fields used as keys
            opts.Schema.For<JoinCustomer>().Duplicate(x => x.City);
            opts.Schema.For<JoinOrder>();
            opts.Schema.For<JoinEmployee>().Duplicate(x => x.City);
        });

        var session = store.LightweightSession();
        _disposables.Add(session);

        session.Store(new JoinCustomer { Id = Guid.NewGuid(), Name = "Alice", City = "Seattle" });
        session.Store(new JoinCustomer { Id = Guid.NewGuid(), Name = "Bob", City = "Portland" });
        session.Store(new JoinCustomer { Id = Guid.NewGuid(), Name = "Charlie", City = "Seattle" });

        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Eve", City = "Seattle" });
        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Frank", City = "Portland" });
        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Grace", City = "Denver" });

        await session.SaveChangesAsync();
        return session;
    }

    private async Task<IDocumentSession> SetupWithBothKeysDuplicated()
    {
        var store = StoreOptions(opts =>
        {
            opts.Schema.For<JoinCustomer>().Duplicate(x => x.City);
            opts.Schema.For<JoinOrder>().Duplicate(x => x.CustomerId).Duplicate(x => x.Amount);
            opts.Schema.For<JoinEmployee>().Duplicate(x => x.City);
        });

        var session = store.LightweightSession();
        _disposables.Add(session);

        var customer1Id = Guid.NewGuid();
        var customer2Id = Guid.NewGuid();
        var customer3Id = Guid.NewGuid();

        session.Store(new JoinCustomer { Id = customer1Id, Name = "Alice", City = "Seattle" });
        session.Store(new JoinCustomer { Id = customer2Id, Name = "Bob", City = "Portland" });
        session.Store(new JoinCustomer { Id = customer3Id, Name = "Charlie", City = "Seattle" });

        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer1Id, Status = "Active", Amount = 100m });
        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer1Id, Status = "Shipped", Amount = 200m });
        session.Store(new JoinOrder { Id = Guid.NewGuid(), CustomerId = customer2Id, Status = "Active", Amount = 300m });

        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Eve", City = "Seattle" });
        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Frank", City = "Portland" });
        session.Store(new JoinEmployee { Id = Guid.NewGuid(), Name = "Grace", City = "Denver" });

        await session.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task GroupJoin_inner_join_with_duplicated_inner_key()
    {
        // JoinOrder.CustomerId is duplicated → ON clause uses d.customer_id (column) on inner side
        var session = await SetupWithDuplicatedInnerKey();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Count(r => r.CustomerName == "Alice").ShouldBe(2);
        results.Count(r => r.CustomerName == "Bob").ShouldBe(1);
        results.ShouldNotContain(r => r.CustomerName == "Charlie");
    }

    [Fact]
    public async Task GroupJoin_inner_join_with_duplicated_outer_key()
    {
        // JoinCustomer.City is duplicated → ON clause uses d.city (column) on outer side
        var session = await SetupWithDuplicatedOuterKey();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinEmployee>(),
                c => c.City,
                e => e.City,
                (c, employees) => new { c, employees })
            .SelectMany(
                x => x.employees,
                (x, e) => new { Customer = x.c.Name, Employee = e.Name, x.c.City })
            .ToListAsync();

        // Seattle: Alice+Eve, Charlie+Eve = 2
        // Portland: Bob+Frank = 1
        results.Count(r => r.City == "Seattle").ShouldBe(2);
        results.Count(r => r.City == "Portland").ShouldBe(1);
        results.ShouldNotContain(r => r.City == "Denver");
    }

    [Fact]
    public async Task GroupJoin_inner_join_with_both_keys_duplicated()
    {
        // Both JoinCustomer.City and JoinEmployee.City are duplicated →
        // ON clause uses d.city on both sides
        var session = await SetupWithBothKeysDuplicated();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinEmployee>(),
                c => c.City,
                e => e.City,
                (c, employees) => new { c, employees })
            .SelectMany(
                x => x.employees,
                (x, e) => new { Customer = x.c.Name, Employee = e.Name, x.c.City })
            .ToListAsync();

        results.Count(r => r.City == "Seattle").ShouldBe(2);
        results.Count(r => r.City == "Portland").ShouldBe(1);
        results.ShouldNotContain(r => r.City == "Denver");
    }

    [Fact]
    public async Task GroupJoin_left_join_with_duplicated_inner_key()
    {
        // JoinOrder.CustomerId is duplicated → LEFT JOIN uses d.customer_id on inner side
        var session = await SetupWithDuplicatedInnerKey();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders.DefaultIfEmpty(),
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = (decimal?)o.Amount })
            .ToListAsync();

        // All customers should appear, including Charlie with null order
        results.Count.ShouldBe(4); // Alice(2) + Bob(1) + Charlie(1 null)
        results.Count(r => r.CustomerName == "Alice").ShouldBe(2);
        results.Count(r => r.CustomerName == "Bob").ShouldBe(1);
        results.Count(r => r.CustomerName == "Charlie").ShouldBe(1);
    }

    [Fact]
    public async Task GroupJoin_left_join_with_both_keys_duplicated()
    {
        // Both City fields duplicated → LEFT JOIN uses d.city on both sides
        var session = await SetupWithBothKeysDuplicated();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinEmployee>(),
                c => c.City,
                e => e.City,
                (c, employees) => new { c, employees })
            .SelectMany(
                x => x.employees.DefaultIfEmpty(),
                (x, e) => new { Customer = x.c.Name, EmployeeName = (string?)e.Name })
            .ToListAsync();

        // All customers appear; Denver employee Grace has no customer match but
        // this is a LEFT join from customers, so Grace won't appear.
        // Alice (Seattle → Eve), Charlie (Seattle → Eve), Bob (Portland → Frank) = 3
        results.Count.ShouldBe(3);
        results.Count(r => r.Customer == "Alice").ShouldBe(1);
        results.Count(r => r.Customer == "Bob").ShouldBe(1);
        results.Count(r => r.Customer == "Charlie").ShouldBe(1);
    }

    [Fact]
    public async Task GroupJoin_inner_join_with_duplicated_guid_key()
    {
        // JoinOrder.CustomerId is a Guid and is duplicated → the column type is uuid
        // This tests that the duplicated Guid column locator works correctly in the ON clause
        var session = await SetupWithDuplicatedInnerKey();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { Customer = x.c.Name, Order = o.Status, o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        var aliceActive = results.FirstOrDefault(r => r.Customer == "Alice" && r.Order == "Active");
        aliceActive.ShouldNotBeNull();
        aliceActive.Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task GroupJoin_inner_join_with_duplicated_projected_fields()
    {
        // Both the join key (CustomerId) and a projected field (Amount) are duplicated
        // Verifies that CTE aliasing works for duplicated fields in the SELECT projection too
        var session = await SetupWithBothKeysDuplicated();

        var results = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.ShouldContain(r => r.CustomerName == "Alice" && r.Amount == 100m);
        results.ShouldContain(r => r.CustomerName == "Alice" && r.Amount == 200m);
        results.ShouldContain(r => r.CustomerName == "Bob" && r.Amount == 300m);
    }

    [Fact]
    public async Task GroupJoin_with_Count_and_duplicated_key()
    {
        // Aggregation (Count) after join on duplicated field
        var session = await SetupWithDuplicatedInnerKey();

        var count = await session.Query<JoinCustomer>()
            .GroupJoin(
                session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, o.Amount })
            .CountAsync();

        count.ShouldBe(3);
    }

    #endregion

    #region With Aggregation

    [Fact]
    public async Task GroupJoin_with_Count()
    {
        await SetupData();

        var count = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
            .CountAsync();

        count.ShouldBe(3);
    }

    [Fact]
    public async Task GroupJoin_with_First()
    {
        await SetupData();

        var result = await _session.Query<JoinCustomer>()
            .GroupJoin(
                _session.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { CustomerName = x.c.Name, OrderAmount = o.Amount })
            .FirstAsync();

        result.ShouldNotBeNull();
        result.CustomerName.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region QuerySession (QueryOnly storage) Tests

    [Fact]
    public async Task GroupJoin_left_join_on_id_with_query_session()
    {
        await SetupData();

        // Use QuerySession (QueryOnly storage) where IdColumn.ShouldSelect returns false.
        // This verifies that d.id is included in the CTE SELECT list even though
        // QueryOnly storage normally excludes it.
        await using var querySession = _store.QuerySession();

        var results = await querySession.Query<JoinCustomer>()
            .GroupJoin(
                querySession.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders.DefaultIfEmpty(),
                (x, o) => new { CustomerName = x.c.Name, OrderStatus = (string?)o.Status })
            .ToListAsync();

        results.Count.ShouldBe(4); // Alice(2) + Bob(1) + Charlie(1 null)
        results.Count(r => r.CustomerName == "Alice").ShouldBe(2);
        results.Count(r => r.CustomerName == "Bob").ShouldBe(1);

        var charlie = results.Single(r => r.CustomerName == "Charlie");
        charlie.OrderStatus.ShouldBeNull();
    }

    [Fact]
    public async Task GroupJoin_inner_join_on_id_with_query_session()
    {
        await SetupData();

        await using var querySession = _store.QuerySession();

        var results = await querySession.Query<JoinCustomer>()
            .GroupJoin(
                querySession.Query<JoinOrder>(),
                c => c.Id,
                o => o.CustomerId,
                (c, orders) => new { c, orders })
            .SelectMany(
                x => x.orders,
                (x, o) => new { x.c.Name, o.Amount })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Count(r => r.Name == "Alice").ShouldBe(2);
    }

    #endregion
}
