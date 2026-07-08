using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

// #4874 (follow-up to #4903/#4905): DocumentStore.DisposeAsync() disposes the tenancy through
// JasperFx's MaybeDisposeAllAsync, which prefers IAsyncDisposable and otherwise falls back to the
// synchronous IDisposable chain. Before this change no tenancy implemented IAsyncDisposable, so async
// store teardown ALWAYS fell back to the synchronous DefaultTenancy.Dispose() -> MartenDatabase.Dispose()
// -> blocking NpgsqlDataSource.Dispose() chain, and the inherited async PostgresqlDatabase.DisposeAsync()
// (guarded by #4905) was unreachable dead code. The tenancy hierarchy is now IAsyncDisposable so the
// async teardown path is actually taken.
//
// NOTE: this does not by itself fix the reporter's owned-data-source ObjectDisposedException storm — that
// is a separate disposal-ordering problem (the data source is disposed before the still-running async
// daemon / durability loops stop renting connections). This change is the correct foundation for that.
public class Bug_4874_async_tenancy_disposal
{
    // Load-bearing: fails on master (DefaultTenancy was IDisposable-only), passes with the fix.
    [Fact]
    public void default_tenancy_is_async_disposable()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug4874_async_tenancy";
        });

        store.Tenancy.ShouldBeAssignableTo<IAsyncDisposable>();
    }

    // Marten owns the data source (connection-string overload). Async teardown must run cleanly and
    // actually dispose the owned data source, so the store is unusable afterward.
    [Fact]
    public async Task disposing_store_async_disposes_a_marten_owned_datasource()
    {
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug4874_async_tenancy_owned";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await using (var session = store.LightweightSession())
        {
            session.Store(new Target { Id = Guid.NewGuid() });
            await session.SaveChangesAsync();
        }

        await store.DisposeAsync();

        // The owned data source was torn down by the async path — any further use that opens a
        // connection throws.
        await Should.ThrowAsync<ObjectDisposedException>(async () =>
        {
            await using var session = store.QuerySession();
            await session.Query<Target>().AnyAsync();
        });
    }

    // A caller-owned data source must survive async store teardown (the async path honors the same
    // OwnsDataSource guard as the sync path). Guards the new tenancy/database async disposal code.
    [Fact]
    public async Task disposing_store_async_does_not_dispose_a_caller_owned_datasource()
    {
        await using var sharedDataSource = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);

        var storeA = DocumentStore.For(opts =>
        {
            opts.Connection(sharedDataSource);
            opts.DatabaseSchemaName = "bug4874_async_tenancy_shared";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        var storeB = DocumentStore.For(opts =>
        {
            opts.Connection(sharedDataSource);
            opts.DatabaseSchemaName = "bug4874_async_tenancy_shared";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await using (var session = storeA.LightweightSession())
        {
            session.Store(new Target { Id = Guid.NewGuid() });
            await session.SaveChangesAsync();
        }

        // Tear down store A via the async path — it must NOT dispose the caller-owned data source.
        await storeA.DisposeAsync();

        await using (var session = storeB.LightweightSession())
        {
            session.Store(new Target { Id = Guid.NewGuid() });
            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }

        await storeB.DisposeAsync();

        await using var conn = sharedDataSource.CreateConnection();
        await Should.NotThrowAsync(async () => await conn.OpenAsync());
    }

    public class Target
    {
        public Guid Id { get; set; }
    }
}
