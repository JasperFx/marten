using System;
using System.Linq;
using System.Text.Json.Serialization;
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

            //For docs on these options see: https://github.com/Tarmil/FSharp.SystemTextJson/blob/master/docs/Customizing.md
            var jsonFSharpOptions =
                JsonFSharpOptions
                    .Default()
                    .WithIncludeRecordProperties()
                    .WithUnionNamedFields()
                    .WithUnionUnwrapSingleCaseUnions()
                    .WithSkippableOptionFields();

            opts.UseSystemTextJsonForSerialization(configure: (jsonOptions) =>
            {
                jsonFSharpOptions.AddToJsonSerializerOptions(jsonOptions);
            });
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
        var order = CreateNewOrder();
        theSession.Store(order);

        // Marten sees that there is no existing identity,
        // so it assigns a new identity
        SpecificationExtensions.ShouldNotBeNull(order.Id);
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<FSharpTypes.Order>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var order = CreateNewOrder();
        theSession.Insert(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<FSharpTypes.Order>().AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        /* Since everything is immutable in F#, change tracking features are typically not used by F# users.
        We use the mutable class below just for the sake of ensuring that change detection works fine when Fsharp DU are used as id types. */

        var order = new ReferenceTypeOrder( FSharpTypes.OrderId.NewId(Guid.NewGuid()));
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        order.CustomerName = "John Doe";
        await theSession.SaveChangesAsync();

        var loaded = await theSession.LoadAsync<ReferenceTypeOrder>(order.Id);
        loaded.CustomerName.ShouldBeNull("updated");
    }

    [Fact]
    public async Task use_within_identity_map()
    {
        var order = CreateNewOrder();
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.IdentitySession();
        var loaded1 = await session.LoadAsync<FSharpTypes.Order>(order.Id);
        var loaded2 = await session.LoadAsync<FSharpTypes.Order>(order.Id);

        loaded1.ShouldBeSameAs(loaded2);
    }

    [Fact]
    public async Task usage_within_dirty_checking()
    {
        //F# types are immutable so this pattern is never used

        var order = new ReferenceTypeOrder( FSharpTypes.OrderId.NewId(Guid.NewGuid()));
        theSession.Insert(order);
        await theSession.SaveChangesAsync();

        await using var session = theStore.DirtyTrackedSession();
        var loaded1 = await session.LoadAsync<ReferenceTypeOrder>(order.Id);
        loaded1.CustomerName = "something else";

        await session.SaveChangesAsync();

        var loaded2 = await theSession.LoadAsync<ReferenceTypeOrder>(order.Id);
        loaded2.CustomerName.ShouldBe(loaded1.CustomerName);
    }

    [Fact]
    public async Task load_document()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<FSharpTypes.Order>(order.Id))
            .CustomerName.ShouldBe(order.CustomerName);
    }

    [Fact]
    public async Task load_many()
    {
        var order1 = CreateNewOrder();
        var order2 = CreateNewOrder();
        var order3 = CreateNewOrder();
        theSession.Store(order1, order2, order3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<FSharpTypes.Order>()
            .Where(x => x.Id.IsOneOf(order1.Id, order2.Id, order3.Id))
            .ToListAsync();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete<FSharpTypes.Order>(order.Id);
        await theSession.SaveChangesAsync();

        SpecificationExtensions.ShouldBeNull((await theSession.LoadAsync<FSharpTypes.Order>(order.Id)));
    }

    [Fact]
    public async Task delete_by_document()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete(order);
        await theSession.SaveChangesAsync();

        SpecificationExtensions.ShouldBeNull((await theSession.LoadAsync<FSharpTypes.Order>(order.Id)));
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<FSharpTypes.Order>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<FSharpTypes.Order>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () => await theSession.LoadAsync<FSharpTypes.Order>(new WrongId(Guid.NewGuid())));
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<FSharpTypes.Order>().FirstOrDefaultAsync(x => x.Id == order.Id);

        loaded
            .CustomerName.ShouldBe(order.CustomerName);
    }

    [Fact]
    public async Task use_in_LINQ_order_clause()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<FSharpTypes.Order>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<FSharpTypes.Order>().Select(x => x.Id).Take(3).ToListAsync();

    }

    [Fact]
    public async Task bulk_writing_async()
    {
        FSharpTypes.Order[] orders = [
            CreateNewOrder(),
            CreateNewOrder(),
            CreateNewOrder(),
            CreateNewOrder(),
            CreateNewOrder()
        ];

        await theStore.BulkInsertDocumentsAsync(orders);
    }

    [Fact]
    public void bulk_writing_sync()
    {
        FSharpTypes.Order[] orders = [

            CreateNewOrder(),
            CreateNewOrder(),
            CreateNewOrder(),
            CreateNewOrder()
        ];

        theStore.BulkInsertDocuments(orders);
    }

    private FSharpTypes.Order CreateNewOrder(string customerName = null)
    {
        var orderId = FSharpTypes.OrderId.NewId(Guid.NewGuid());
        return new FSharpTypes.Order(orderId, customerName ?? Guid.NewGuid().ToString());
    }
}

public class ReferenceTypeOrder
{
    public ReferenceTypeOrder(FSharpTypes.OrderId id, string customerName = null)
    {
        Id = id;
        CustomerName = customerName;
    }
    public FSharpTypes.OrderId Id { get; }

    public string CustomerName { get; set; }
}
