using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

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
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();
        var originalCode = "PROD-001";
        var newCode = "PROD-999";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, originalCode));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }


        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ProductCodeChanged(streamId, newCode));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        await using (var session = theStore.LightweightSession())
        {
            var product = await session.Events.FetchLatest<Product, ProductCode>(new ProductCode(newCode), TestContext.Current.CancellationToken);
            product.ShouldNotBeNull();
            product.Code.Value.ShouldBe(newCode);
        }
    }

    [Fact]
    public async Task bug_5041_renaming_the_natural_key_retires_the_previous_row_inline()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, "PROD-001"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await naturalKeysForStream(streamId)).ShouldBe(["PROD-001"]);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new ProductCodeChanged(streamId, "PROD-999"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Before #5041 the retired PROD-001 row survived alongside PROD-999, permanently
        // squatting on its slot in the natural key table's primary key.
        (await naturalKeysForStream(streamId)).ShouldBe(["PROD-999"]);
    }

    [Fact]
    public async Task bug_5041_the_previous_natural_key_row_is_not_resurrected_by_a_rebuild()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, "PROD-001"));
            session.Events.Append(streamId, new ProductCodeChanged(streamId, "PROD-999"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        // The rebuild path (StartProjectionBatchAsync -> QueueUpsertsForEvents) replays both
        // events through the same upsert builder, so it has to retire PROD-001 too.
        (await naturalKeysForStream(streamId)).ShouldBe(["PROD-999"]);

        await using var query = theStore.LightweightSession();
        var product = await query.Events.FetchLatest<Product, ProductCode>(new ProductCode("PROD-999"), TestContext.Current.CancellationToken);
        product.ShouldNotBeNull();
        product.Code.Value.ShouldBe("PROD-999");
    }

    [Fact]
    public async Task bug_5041_a_retired_natural_key_can_be_claimed_by_another_stream()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(first, new ProductRegistered(first, "PROD-001"));
            session.Events.Append(first, new ProductCodeChanged(first, "PROD-999"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(second, new ProductRegistered(second, "PROD-001"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await naturalKeysForStream(first)).ShouldBe(["PROD-999"]);
        (await naturalKeysForStream(second)).ShouldBe(["PROD-001"]);

        // The snapshot is Async, so the documents only exist once the daemon has run.
        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        await using var query = theStore.LightweightSession();
        var reused = await query.Events.FetchLatest<Product, ProductCode>(new ProductCode("PROD-001"), TestContext.Current.CancellationToken);
        reused.ShouldNotBeNull();
        reused.Id.ShouldBe(second);
    }

    private async Task<string[]> naturalKeysForStream(Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select natural_key_value from {schemaName}.mt_natural_key_product where stream_id = :id order by natural_key_value";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = streamId;
        cmd.Parameters.Add(parameter);

        var values = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(await reader.GetFieldValueAsync<string>(0));
        }

        return values.ToArray();
    }
}
