using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;

namespace ValueTypeTests.Vogen;

public class int_based_document_operations : IAsyncLifetime
{
    private readonly DocumentStore theStore;

    public int_based_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed6";
        });

        theSession = theStore.LightweightSession();
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Order));
    }

    public async Task DisposeAsync()
    {
        await theStore.DisposeAsync();
        theSession?.Dispose();
    }

    private IDocumentSession theSession;

    [Fact]
    public void store_document_will_assign_the_identity()
    {
        var order = new Order();
        theSession.Store(order);

        order.Id.ShouldNotBeNull();
        ShouldBeTestExtensions.ShouldNotBe(order.Id.Value.Value, 0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var order = new Order();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Order[] invoices = [
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Order[] invoices = [
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()},
            new Order{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var order = new Order();
        theSession.Insert(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var order = new Order();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        order.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Order>(order.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var order = new Order();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Order>(order.Id);
        var loaded2 = await session.LoadAsync<Order>(order.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var order = new Order();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Order>(order.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Order>(order.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order>(order.Id))
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var order1 = new Order{Name = Guid.NewGuid().ToString()};
        var order2 = new Order{Name = Guid.NewGuid().ToString()};
        var order3 = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order1, order2, order3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Order>().Where(x => x.Id.IsOneOf(order1.Id, order2.Id, order3.Id)).ToListAsync();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete<Order>(order.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order>(order.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete(order);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order>(order.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order>(WrongId.From(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order>().FirstOrDefaultAsync(x => x.Id == order.Id);

        loaded
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var order = new Order{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

[ValueObject<int>]
public partial struct OrderId;

public class Order
{
    public OrderId? Id { get; set; }
    public string Name { get; set; }
}

