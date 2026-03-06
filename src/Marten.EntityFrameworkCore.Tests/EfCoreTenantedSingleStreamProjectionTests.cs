using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public abstract class EfCoreTenantedSingleStreamProjectionTestsBase: IAsyncLifetime
{
    protected DocumentStore Store = null!;

    protected abstract ProjectionLifecycle Lifecycle { get; }

    public async Task InitializeAsync()
    {
        Store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"efcore_tss_{Lifecycle.ToString().ToLower()}";
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Add(new TenantedOrderAggregate(), Lifecycle);
        });

        await Store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        Store?.Dispose();
        return Task.CompletedTask;
    }

    protected virtual Task WaitForProjectionAsync() => Task.CompletedTask;

    private string SchemaName => $"efcore_tss_{Lifecycle.ToString().ToLower()}";

    protected async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var setSchema = conn.CreateCommand();
        setSchema.CommandText = $"SET search_path TO {SchemaName}";
        await setSchema.ExecuteNonQueryAsync();
        return conn;
    }

    [Fact]
    public async Task tenant_id_is_written_to_ef_core_table()
    {
        var orderId = Guid.NewGuid();
        await using var session = Store.LightweightSession("alpha");
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Alice", 100.00m, 3));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, tenant_id FROM ef_tenanted_orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Alice");
        reader.GetString(1).ShouldBe("alpha");
    }

    [Fact]
    public async Task different_tenants_get_isolated_data()
    {
        var alphaOrderId = Guid.NewGuid();
        var betaOrderId = Guid.NewGuid();

        await using (var alphaSession = Store.LightweightSession("alpha"))
        {
            alphaSession.Events.StartStream(alphaOrderId,
                new OrderPlaced(alphaOrderId, "AlphaCustomer", 50.00m, 1));
            await alphaSession.SaveChangesAsync();
        }

        await using (var betaSession = Store.LightweightSession("beta"))
        {
            betaSession.Events.StartStream(betaOrderId,
                new OrderPlaced(betaOrderId, "BetaCustomer", 75.00m, 2));
            await betaSession.SaveChangesAsync();
        }

        await WaitForProjectionAsync();

        await using var conn = await OpenConnectionAsync();

        // Check alpha
        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT customer_name, tenant_id FROM ef_tenanted_orders WHERE id = @id";
        cmd1.Parameters.AddWithValue("id", alphaOrderId);
        await using var reader1 = await cmd1.ExecuteReaderAsync();
        (await reader1.ReadAsync()).ShouldBeTrue();
        reader1.GetString(0).ShouldBe("AlphaCustomer");
        reader1.GetString(1).ShouldBe("alpha");
        await reader1.CloseAsync();

        // Check beta
        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT customer_name, tenant_id FROM ef_tenanted_orders WHERE id = @id";
        cmd2.Parameters.AddWithValue("id", betaOrderId);
        await using var reader2 = await cmd2.ExecuteReaderAsync();
        (await reader2.ReadAsync()).ShouldBeTrue();
        reader2.GetString(0).ShouldBe("BetaCustomer");
        reader2.GetString(1).ShouldBe("beta");
    }

    [Fact]
    public async Task subsequent_appends_preserve_tenant_id()
    {
        var orderId = Guid.NewGuid();

        await using var session = Store.LightweightSession("alpha");
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Carol", 200.00m, 5));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        session.Events.Append(orderId, new OrderShipped(orderId));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, is_shipped, tenant_id FROM ef_tenanted_orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Carol");
        reader.GetBoolean(1).ShouldBeTrue();
        reader.GetString(2).ShouldBe("alpha");
    }
}

public class EfCoreTenantedSingleStreamProjectionInlineTests: EfCoreTenantedSingleStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;
}

public class EfCoreTenantedSingleStreamProjectionAsyncTests: EfCoreTenantedSingleStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    protected override async Task WaitForProjectionAsync()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await Store.WaitForNonStaleProjectionDataAsync(15.Seconds());
    }
}
