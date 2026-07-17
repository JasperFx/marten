using System;
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

public class Bug_4966_natural_key_update_on_projection_rebuild: DaemonContext
{
    private const string schemaName = "rebuild_natural_key_update";

    public Bug_4966_natural_key_update_on_projection_rebuild(ITestOutputHelper output) : base(output)
    {
    }

    public sealed record ProductCode(string Value);

    public sealed record ProductRegistered(Guid ProductId, string ProductCode);

    public sealed record ProductCodeChanged(Guid ProductId, string NewProductCode);

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
        public static Product Apply(ProductCodeChanged e, Product product)
        {
            return product with
            {
                Code = new ProductCode(e.NewProductCode)
            };
        }
    }

    private static void ConfigureStore(StoreOptions opts)
    {
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
            session.Events.Append(streamId, new ProductCodeChanged(streamId, newCode));
            await session.SaveChangesAsync();
        }

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        await using (var session = theStore.LightweightSession())
        {
            var product = await session.Events.FetchLatest<Product, ProductCode>(new ProductCode(newCode));
            product.ShouldNotBeNull();
            product.Code.Value.ShouldBe(newCode);
        }
    }
}
