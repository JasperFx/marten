using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_4788_natrual_key_table_not_populated_on_rebuild: OneOffConfigurationsContext
{
    private const string schemaName = "bug_4788";

    public sealed record OrderNumber(string Value);

    public sealed record OrderPlaced(Guid OrderId, string OrderNumber);

    public sealed record OrderShipped(Guid OrderId, string TrackingNumber);

    public sealed record Order
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public OrderNumber Number { get; set; }

        public string? TrackingNumber { get; set; }

        [NaturalKeySource]
        public static Order Create(OrderPlaced e)
        {
            return new Order
            {
                Id = e.OrderId,
                Number = new OrderNumber(e.OrderNumber)
            };
        }

        public static Order Apply(OrderShipped e, Order order)
        {
            return new Order
            {
                TrackingNumber = e.TrackingNumber
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

        opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);
    }

    private async Task<object> ExecuteScalar(string sql)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<DataTable> GetData(string sql)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);
        return dataTable;
    }

    [Fact]
    public async Task rebuild_snapshot_should_populate_naturalkey_table()
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Order>(streamId, new OrderPlaced(streamId, "12345"));
        await session.SaveChangesAsync();

        var anotherStore = SeparateStore(ConfigureStore);
        var anotherSession = anotherStore.LightweightSession();
        var order1 = await anotherSession.Events.FetchLatest<Order, OrderNumber>(new OrderNumber("12345"));
        order1.ShouldNotBeNull();

        var naturalKeyTableName = $"{schemaName}.mt_natural_key_order";
        var natrualKeys = await GetData($"SELECT * FROM {naturalKeyTableName}");
        natrualKeys.Rows.Count.ShouldBe(1);

        await ExecuteScalar($"DELETE FROM {naturalKeyTableName}");
        natrualKeys = await GetData($"SELECT * FROM {naturalKeyTableName}");
        natrualKeys.Rows.Count.ShouldBe(0);

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var daemon = await SeparateStore(ConfigureStore).BuildProjectionDaemonAsync();
        await daemon.PrepareForRebuildsAsync();
        await daemon.RebuildProjectionAsync($"{nameof(Bug_4788_natrual_key_table_not_populated_on_rebuild)}.order", CancellationToken.None);

        var afterRebuildStore = SeparateStore(ConfigureStore);
        var afterRebuildSession = afterRebuildStore.LightweightSession();
        var order2 = await afterRebuildSession.Events.FetchLatest<Order, OrderNumber>(new OrderNumber("12345"));
        order2.ShouldNotBeNull();

        natrualKeys = await GetData($"SELECT * FROM {naturalKeyTableName}");
        natrualKeys.Rows.Count.ShouldBe(1);
    }
}
