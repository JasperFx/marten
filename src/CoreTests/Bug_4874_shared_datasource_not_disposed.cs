using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

// #4874: Marten must not dispose a caller-owned NpgsqlDataSource. Two stores sharing one external
// data source is the deterministic shape of the reported "pooled connection disposed underneath an
// in-flight operation" abort — before the fix, disposing store A tore down the shared data source and
// aborted store B's next operation.
public class Bug_4874_shared_datasource_not_disposed
{
    [Fact]
    public async Task disposing_one_store_does_not_abort_another_sharing_the_datasource()
    {
        await using var sharedDataSource = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);

        var storeA = DocumentStore.For(opts =>
        {
            opts.Connection(sharedDataSource);
            opts.DatabaseSchemaName = "bug4874_shared";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        var storeB = DocumentStore.For(opts =>
        {
            opts.Connection(sharedDataSource);
            opts.DatabaseSchemaName = "bug4874_shared";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        // Both stores are live against the shared data source.
        await using (var session = storeA.LightweightSession())
        {
            session.Store(new Target { Id = System.Guid.NewGuid() });
            await session.SaveChangesAsync();
        }

        await using (var session = storeB.QuerySession())
        {
            (await session.Query<Target>().CountAsync()).ShouldBeGreaterThanOrEqualTo(0);
        }

        // Tear down store A. It shares the caller-owned data source with store B, so it must NOT
        // dispose it (per the Connection(NpgsqlDataSource) contract).
        storeA.Dispose();

        // Before the fix this threw ObjectDisposedException / a socket abort because store A had
        // disposed the shared NpgsqlDataSource.
        await using (var session = storeB.LightweightSession())
        {
            session.Store(new Target { Id = System.Guid.NewGuid() });
            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }

        storeB.Dispose();

        // The caller still owns the data source — it was never disposed by Marten and remains usable.
        await using var conn = sharedDataSource.CreateConnection();
        await Should.NotThrowAsync(async () => await conn.OpenAsync());
    }

    public class Target
    {
        public System.Guid Id { get; set; }
    }
}
