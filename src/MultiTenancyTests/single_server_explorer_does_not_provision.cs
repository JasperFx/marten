using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #5400 — the single-server counterpart to <see cref="sharded_explorer_does_not_provision"/>, and the
/// more dramatic half of the defect.
/// </summary>
/// <remarks>
/// <para>
/// <c>SingleServerMultiTenancy</c> keeps one PostgreSQL database per tenant. On a cache miss its
/// <c>GetTenantAsync</c> falls back to <c>databaseName = tenantId</c> and calls the Weasel base
/// <c>SingleServerDatabaseCollection.FindOrCreateDatabase</c>, which checks <c>DatabaseExists</c> and
/// otherwise issues <c>CREATE DATABASE</c>. So an explorer read pointed at an unrecognized tenant id did
/// not merely fail — it created a whole database named after the id.
/// </para>
/// <para>
/// <c>FindOrCreateDatabase</c> itself is deliberately left alone: create-on-demand is its documented
/// purpose and is pinned by <c>SingleServerMultiTenancyTests.build_database_on_the_fly</c>. Only the
/// explorer's resolution changed.
/// </para>
/// </remarks>
[Collection("multi-tenancy")]
public class single_server_explorer_does_not_provision: IAsyncLifetime
{
    private const string NeverSeen = "explorer_never_seen_db";
    private const string Known = "explorer_known_db";

    private IDocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await DropDatabaseIfExists(NeverSeen);
        await DropDatabaseIfExists(Known);

        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString,
                x => x.WithTenants(Known));

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DisableNpgsqlLogging = true;
            opts.RegisterDocumentType<Target>();
        });
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    [Fact]
    public async Task explorer_status_read_does_not_create_a_database_for_an_unknown_tenant()
    {
        (await DatabaseExists(NeverSeen)).ShouldBeFalse("precondition: the database must not exist yet");

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await ((IEventStore)_store).GetProjectionStatusesAsync(NeverSeen, CancellationToken.None));

        // The assertion that matters. Before #5400 the explorer read ran CREATE DATABASE for this id.
        (await DatabaseExists(NeverSeen))
            .ShouldBeFalse("a diagnostics read must not create a PostgreSQL database");
    }

    [Fact]
    public async Task explorer_status_read_still_resolves_a_registered_tenant()
    {
        // The other half: the fix must not answer "unknown" for a tenant that really is configured.
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var statuses = await ((IEventStore)_store)
            .GetProjectionStatusesAsync(Known, CancellationToken.None);

        statuses.ShouldNotBeNull();
    }

    private static async Task DropDatabaseIfExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.KillIdleSessions(databaseName);
        await conn.DropDatabase(databaseName);
    }

    private static async Task<bool> DatabaseExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return await conn.DatabaseExists(databaseName);
    }
}
