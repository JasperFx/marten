using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #5400 — the event store explorer's read APIs must not provision anything.
/// </summary>
/// <remarks>
/// <para>
/// The four explorer reads used to resolve their database through <c>Tenancy.FindOrCreateDatabase</c>,
/// and <see cref="ShardedTenancy"/> takes the "or create" half literally: an unknown tenant id falls
/// through to <c>findOrAssignTenantDatabaseAsync</c>, which picks a shard, writes an assignment row, and
/// runs partition + per-tenant sequence DDL for it. So a monitoring console polling the status of a
/// retired or mistyped tenant id would bring that tenant into existence — a read with a write for a side
/// effect.
/// </para>
/// <para>
/// Note what is deliberately NOT asserted here: that a <em>soft-deleted</em> tenant provisions. It does
/// not, and never did — #4607 makes a disabled assignment throw <c>UnknownTenantIdException</c> under the
/// advisory lock rather than resurrect the tenant on a possibly-different shard. The exposure was always
/// the never-seen id.
/// </para>
/// </remarks>
[Collection("sharded-tenancy")]
public class sharded_explorer_does_not_provision: IAsyncLifetime
{
    private const string NeverSeen = "explorer_never_seen_tenant";

    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_explorer_does_not_provision(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedTenancyFixture.cleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private void CreateStore()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";

                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.RegisterDocumentType<Target>();
        });
    }

    [Fact]
    public async Task explorer_status_read_does_not_assign_an_unknown_tenant()
    {
        CreateStore();
        var sharded = (ShardedTenancy)_store.Options.Tenancy;

        // Seed the pool so the databases exist and the ONLY thing missing is the tenant assignment.
        await _store.Options.Tenancy.BuildDatabases();

        (await sharded.FindDatabaseForTenantAsync(NeverSeen, CancellationToken.None))
            .ShouldBeNull("precondition: the tenant must not be assigned before the explorer read");

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await ((IEventStore)_store).GetProjectionStatusesAsync(NeverSeen, CancellationToken.None));

        // The assertion that matters. Before #5400 this call assigned the tenant to a shard and ran its
        // partition + sequence DDL, so the row was here afterwards.
        (await sharded.FindDatabaseForTenantAsync(NeverSeen, CancellationToken.None))
            .ShouldBeNull("a diagnostics read must not assign a tenant to a shard");
    }

    [Fact]
    public async Task explorer_status_read_still_resolves_a_genuinely_assigned_tenant()
    {
        CreateStore();

        // The other half: the fix must not answer "unknown" for a tenant that really is registered.
        await _store.Advanced.AddTenantToShardAsync("explorer_known_tenant", CancellationToken.None);

        var statuses = await ((IEventStore)_store)
            .GetProjectionStatusesAsync("explorer_known_tenant", CancellationToken.None);

        statuses.ShouldNotBeNull();
    }
}
