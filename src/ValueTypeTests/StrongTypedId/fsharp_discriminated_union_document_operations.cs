using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Exceptions;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;

namespace ValueTypeTests.StrongTypedId;

public class fsharp_discriminated_union_document_operations: IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;
    private readonly IDocumentSession theSession;

    public fsharp_discriminated_union_document_operations()
    {
        var schemaName = "strong_typed_fsharp";
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        options.AutoCreateSchemaObjects = AutoCreate.All;
        options.NameDataLength = 100;
        options.DatabaseSchemaName = schemaName;

        options.ApplicationAssembly = GetType().Assembly;
        options.GeneratedCodeMode = TypeLoadMode.Auto;
        options.GeneratedCodeOutputPath =
            AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
                .AppendPath("Internal", "Generated");

        //For docs on these options see: https://github.com/Tarmil/FSharp.SystemTextJson/blob/master/docs/Customizing.md
        var jsonFSharpOptions =
            JsonFSharpOptions
                .Default()
                .WithIncludeRecordProperties()
                .WithUnionNamedFields()
                .WithUnionUnwrapSingleCaseUnions()
                .WithSkippableOptionFields();

        options.UseSystemTextJsonForSerialization(configure: (jsonOptions) =>
        {
            jsonFSharpOptions.AddToJsonSerializerOptions(jsonOptions);
        });

        // clean-up
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        conn.Open();
        conn.CreateCommand($"drop schema if exists {schemaName} cascade")
            .ExecuteNonQuery();


        theStore = new DocumentStore(options);
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
        order.Id.ShouldNotBeNull();
    }

    [Fact]
    public async Task store_a_document_smoke_test()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<FSharpTypes.Order>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task insert_a_document_smoke_test()
    {
        var order = CreateNewOrder();
        theSession.Insert(order);

        await theSession.SaveChangesAsync();

        (await theSession.Query<FSharpTypes.Order>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task update_a_document_smoke_test()
    {
        /* Since everything is immutable in F#, change tracking features are typically not used by F# users.
        We use the mutable class below just for the sake of ensuring that change detection works fine when Fsharp DU are used as id types. */

        var order = new ReferenceTypeOrder(FSharpTypes.OrderId.NewId(Guid.NewGuid()));
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
        var order = new ReferenceTypeOrder(FSharpTypes.OrderId.NewId(Guid.NewGuid()));
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
    public async Task load_many_LINQ_is_one_of_clause()
    {
        var order1 = CreateNewOrder();
        var order2 = CreateNewOrder();
        var order3 = CreateNewOrder();
        theSession.Store(order1, order2, order3);

        await theSession.SaveChangesAsync();

        var results = await theSession
            .Query<FSharpTypes.Order>()
            .Where(x => x.Id.IsOneOf(order1.Id, order2.Id))
            .Select(x => x.Id)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results.ShouldHaveTheSameElementsAs(order1.Id, order2.Id);
    }

    [Fact]
    public async Task delete_by_id()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete<FSharpTypes.Order>(order.Id);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<FSharpTypes.Order>(order.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task delete_by_document()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        theSession.Delete(order);
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<FSharpTypes.Order>(order.Id)).ShouldBeNull();
    }


    [Theory]
    [InlineData(1)]
    [InlineData(1L)]
    [InlineData("something")]
    public async Task throw_id_mismatch_when_wrong(object id)
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
            await theSession.LoadAsync<FSharpTypes.Order>(id));
    }

    [Fact]
    public async Task can_not_use_just_guid_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
            await theSession.LoadAsync<FSharpTypes.Order>(Guid.NewGuid()));
    }

    [Fact]
    public async Task can_not_use_another_guid_based_strong_typed_id_as_id()
    {
        await Should.ThrowAsync<DocumentIdTypeMismatchException>(async () =>
            await theSession.LoadAsync<FSharpTypes.Order>(new WrongId(Guid.NewGuid())));
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
        var order1 =
            new FSharpTypes.Order(FSharpTypes.OrderId.NewId(Guid.Parse("b019276a-31a1-4d5b-9a1b-aa9c272261bd")),
                "customer1");
        var order2 =
            new FSharpTypes.Order(FSharpTypes.OrderId.NewId(Guid.Parse("89ec7fe6-79c7-460e-9cdc-ddeb1f281095")),
                "customer2");
        theSession.Store(order1);
        theSession.Store(order2);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<FSharpTypes.Order>().OrderBy(x => x.Id).ToListAsync();
        // fetch ids in memory to confirm correct order
        var loadedIds = loaded.Select(x => x.Id).ToList();
        loadedIds.ShouldHaveTheSameElementsAs(order2.Id, order1.Id);
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var order = CreateNewOrder();
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<FSharpTypes.Order>().Select(x => x.Id).FirstOrDefaultAsync();
        loaded.ShouldBe(order.Id);
    }

    [Fact]
    public async Task bulk_writing_async()
    {
        FSharpTypes.Order[] orders =
        [
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
        FSharpTypes.Order[] orders =
        [
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
