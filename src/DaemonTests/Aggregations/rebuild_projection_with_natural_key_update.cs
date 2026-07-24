using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Aggregations;

public class NaturalKeyRebuildTests: DaemonContext
{
    private const string schemaName = "rebuild_natural_key_update";

    public NaturalKeyRebuildTests(ITestOutputHelper output) : base(output)
    {
    }

    public sealed record ProductCode(string Value);
    public sealed record ProductRegistered(Guid ProductId, string ProductCode);
    public sealed record ProductCodeChanged1(Guid ProductId, string NewProductCode);
    public sealed record ProductCodeChanged2(Guid ProductId, string NewProductCode);

    public sealed record Product
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public ProductCode Code { get; set; }

        [NaturalKeySource]
        public static Product Create(ProductRegistered e)
        {
            return new Product
            {
                Id = e.ProductId,
                Code = new ProductCode(e.ProductCode)
            };
        }

        [NaturalKeySource]
        public void Apply(ProductCodeChanged1 e)
        {
            Code = new ProductCode(e.NewProductCode);
        }

        [NaturalKeySource]
        public static Product Apply(IEvent<ProductCodeChanged2> e, Product product)
        {
            return product with
            {
                Code = new ProductCode(e.Data.NewProductCode)
            };
        }
    }

    private static void ConfigureStore(StoreOptions opts)
    {
        opts.Advanced.Migrator.NameDataLength = 100;
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = schemaName;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Projections.Snapshot<Product>(SnapshotLifecycle.Async);
    }

    [Fact]
    public async Task bug_4966_natural_key_should_be_updated_during_rebuild()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        var originalCode = "PROD-001";
        var newCode = "PROD-999";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, originalCode));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ProductCodeChanged1(streamId, newCode));
            await session.SaveChangesAsync();
        }

        var rebuildStore = SeparateStore(ConfigureStore);
        var daemon = await rebuildStore.BuildProjectionDaemonAsync();
        await daemon.PrepareForRebuildsAsync();
        var projectionName = rebuildStore.Options.Projections.All.Single().Name;
        await daemon.RebuildProjectionAsync(projectionName, CancellationToken.None);

        var afterRebuildStore = SeparateStore(ConfigureStore);
        await using var afterRebuildSession = afterRebuildStore.LightweightSession();


        var product = await afterRebuildSession.Events.FetchLatest<Product, ProductCode>(new ProductCode(newCode));
        product.ShouldNotBeNull();
        product.Code.Value.ShouldBe(newCode);

        var naturalKeyTableName = $"{schemaName}.mt_natural_key_product";
        var keyRows = await GetNaturalKeyRows(naturalKeyTableName, streamId);
        keyRows.Rows.Count.ShouldBe(1, $"Expected exactly one natural key row for stream {streamId}, but found {keyRows.Rows.Count}");
        keyRows.Rows[0]["natural_key"].ShouldBe(newCode);
    }

    [Fact]
    public async Task bug_5041_natural_key_should_be_updated_during_rebuild_when_handler_is_ievent()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        var originalCode = "PROD-001";
        var newCode = "PROD-999";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, originalCode));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ProductCodeChanged2(streamId, newCode));
            await session.SaveChangesAsync();
        }

        var rebuildStore = SeparateStore(ConfigureStore);
        var daemon = await rebuildStore.BuildProjectionDaemonAsync();
        await daemon.PrepareForRebuildsAsync();
        var projectionName = rebuildStore.Options.Projections.All.Single().Name;
        await daemon.RebuildProjectionAsync(projectionName, CancellationToken.None);

        var afterRebuildStore = SeparateStore(ConfigureStore);
        await using var afterRebuildSession = afterRebuildStore.LightweightSession();


        var product = await afterRebuildSession.Events.FetchLatest<Product, ProductCode>(new ProductCode(newCode));
        product.ShouldNotBeNull();
        product.Code.Value.ShouldBe(newCode);

        var naturalKeyTableName = $"{schemaName}.mt_natural_key_product";
        var keyRows = await GetNaturalKeyRows(naturalKeyTableName, streamId);
        keyRows.Rows.Count.ShouldBe(1, $"Expected exactly one natural key row for stream {streamId}, but found {keyRows.Rows.Count}");
        keyRows.Rows[0]["natural_key"].ShouldBe(newCode);
    }


    private async Task<DataTable> GetNaturalKeyRows(string tableName, Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {tableName} WHERE stream_id = '{streamId}'";
        await using var reader = await cmd.ExecuteReaderAsync();
        var dt = new DataTable();
        dt.Load(reader);
        return dt;
    }
}
