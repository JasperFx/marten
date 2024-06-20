using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;

namespace ValueTypeTests.StrongTypedId;

public class fsharp_discriminated_union_document_operations: IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;
    private readonly IDocumentSession theSession;

    public fsharp_discriminated_union_document_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";

            opts.ApplicationAssembly = GetType().Assembly;
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
            opts.GeneratedCodeOutputPath =
                AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory().AppendPath("Internal", "Generated");

            opts.RegisterValueType(typeof(OrderId));
        });

        theSession = theStore.LightweightSession();
    }

    public void Dispose()
    {
        theStore?.Dispose();
        theSession?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (theStore != null)
        {
            await theStore.DisposeAsync();
        }
    }

      [Fact]
    public void store_document_will_assign_the_identity()
    {
        var order = new Order();
        theSession.Store(order);

        // Marten sees that there is no existing identity,
        // so it assigns a new identity
        SpecificationExtensions.ShouldNotBeNull(order.Id);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var invoice = new Invoice2();
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Invoice2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var invoice = new Invoice2();
        theSession.Insert(invoice);

        await theSession.SaveChangesAsync();

        (await theSession.Query<Invoice2>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        var invoice = new Invoice2();
        theSession.Insert(invoice);
        await theSession.SaveChangesAsync();

        invoice.Name = "updated";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<Invoice2>(invoice.Id);
        loaded.Name.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var invoice = new Invoice2();
        theSession.Insert(invoice);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<Invoice2>(invoice.Id);
        var loaded2 = await session.LoadAsync<Invoice2>(invoice.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        var invoice = new Invoice2();
        theSession.Insert(invoice);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<Invoice2>(invoice.Id);
        loaded1.Name = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<Invoice2>(invoice.Id);
        loaded2.Name.ShouldBe(loaded1.Name);
    }

    [Fact]
    public async Task load_document()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<Invoice2>(invoice.Id))
            .Name.ShouldBe(invoice.Name);
    }

    [Fact]
    public async Task load_many()
    {
        var invoice1 = new Invoice2{Name = Guid.NewGuid().ToString()};
        var invoice2 = new Invoice2{Name = Guid.NewGuid().ToString()};
        var invoice3 = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice1, invoice2, invoice3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<Invoice2>()
            .Where(x => x.Id.IsOneOf(invoice1.Id, invoice2.Id, invoice3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        theSession.Delete<Invoice2>(invoice.Id);
        await theSession.SaveChangesAsync();

        SpecificationExtensions.ShouldBeNull((await theSession.LoadAsync<Invoice2>(invoice.Id)));
    }

    [Fact]
    public async Task delete_by_document()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        theSession.Delete(invoice);
        await theSession.SaveChangesAsync();

        SpecificationExtensions.ShouldBeNull((await theSession.LoadAsync<Invoice2>(invoice.Id)));
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Invoice2>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Invoice2>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<Invoice2>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Invoice2>().FirstOrDefaultAsync(x => x.Id == invoice.Id);

        loaded
            .Name.ShouldBe(invoice.Name);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Invoice2>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var invoice = new Invoice2{Name = Guid.NewGuid().ToString()};
        theSession.Store(invoice);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<Invoice2>().Select(x => x.Id).Take(3).ToListAsync();

    }

    [Fact]
    public async Task bulk_writing_async()
    {
        Invoice2[] invoices = [
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()}
        ];

        await theStore.BulkInsertDocumentsAsync(invoices);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        Invoice2[] invoices = [
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()},
            new Invoice2{Name = Guid.NewGuid().ToString()}
        ];

        theStore.BulkInsertDocuments(invoices);
    }
}

public class Order
{
    public OrderId Id { get; }
    public string CustomerName { get; set; }
}
