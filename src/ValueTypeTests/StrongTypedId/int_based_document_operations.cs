using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
using Vogen;

namespace ValueTypeTests.StrongTypedId;

public class int_based_document_operations : IAsyncLifetime
{
    private readonly DocumentStore theStore;

    public int_based_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";
        });

        theSession = theStore.LightweightSession();
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Order2));
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
        var order = new Order2();
        theSession.Store(order);

        order.Id.ShouldNotBeNull();
        ShouldBeTestExtensions.ShouldNotBe(order.Id.Value.Value, 0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var order = new Order2();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Order2[] invoices = [
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Order2[] invoices = [
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()},
            new Order2{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var order = new Order2();
        theSession.Insert(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var order = new Order2();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        order.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Order2>(order.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var order = new Order2();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Order2>(order.Id);
        var loaded2 = await session.LoadAsync<Order2>(order.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var order = new Order2();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Order2>(order.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Order2>(order.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order2>(order.Id))
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var order1 = new Order2{Name = Guid.NewGuid().ToString()};
        var order2 = new Order2{Name = Guid.NewGuid().ToString()};
        var order3 = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order1, order2, order3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Order2>().Where(x => x.Id.IsOneOf(order1.Id, order2.Id, order3.Id)).ToListAsync();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete<Order2>(order.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order2>(order.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete(order);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order2>(order.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order2>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order2>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order2>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order2>().FirstOrDefaultAsync(x => x.Id == order.Id);

        loaded
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order2>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var order = new Order2{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order2>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

#region sample_order2_with_STRONG_TYPED_identifier

[StronglyTypedId(Template.Int)]
public partial struct Order2Id;

public class Order2
{
    public Order2Id? Id { get; set; }
    public string Name { get; set; }
}

#endregion

public class Order3
{
    public Order2Id Id { get; set; }
    public string Name { get; set; }
}

public class int_based_document_operations_with_non_nullable_id : IAsyncLifetime
{
    private readonly DocumentStore theStore;

    public int_based_document_operations_with_non_nullable_id()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";
        });

        theSession = theStore.LightweightSession();
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Order3));
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
        var order = new Order3();
        theSession.Store(order);

        order.Id.ShouldNotBeNull();
        ShouldBeTestExtensions.ShouldNotBe(order.Id.Value, 0);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var order = new Order3();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Order3[] invoices = [
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Order3[] invoices = [
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()},
            new Order3{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var order = new Order3();
        theSession.Insert(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Order3>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var order = new Order3();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        order.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Order3>(order.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var order = new Order3();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Order3>(order.Id);
        var loaded2 = await session.LoadAsync<Order3>(order.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var order = new Order3();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Order3>(order.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Order3>(order.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order3>(order.Id))
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var order1 = new Order3{Name = Guid.NewGuid().ToString()};
        var Order3 = new Order3{Name = Guid.NewGuid().ToString()};
        var order3 = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order1, Order3, order3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<Order3>().Where(x => x.Id.IsOneOf(order1.Id, Order3.Id, order3.Id)).ToListAsync();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete<Order3>(order.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order3>(order.Id))
            .ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete(order);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Order3>(order.Id))
            .ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order3>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order3>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Order3>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order3>().FirstOrDefaultAsync(x => x.Id == order.Id);

        loaded
            .Name.ShouldBe(order.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order3>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var order = new Order3{Name = Guid.NewGuid().ToString()};
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Order3>().Select(x => x.Id).Take(3).ToListAsync();

    }
}


