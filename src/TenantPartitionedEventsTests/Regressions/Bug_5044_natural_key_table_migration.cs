#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #5044. A [NaturalKey] aggregate combined with conjoined event tenancy and
/// UseTenantPartitionedEvents produced a schema migration that could not be applied a second time:
///
///     ALTER TABLE marten.mt_natural_key_casestream
///         DROP CONSTRAINT IF EXISTS fk_mt_natural_key_casestream_stream_tenant_1;
///     42P16: cannot drop inherited constraint "fk_mt_natural_key_casestream_stream_tenant_1"
///
/// mt_natural_key_X carries a composite foreign key to mt_streams. Under
/// UseTenantPartitionedEvents mt_streams is a partitioned table, and PostgreSQL clones such a
/// foreign key into one extra pg_constraint row per partition of the *referenced* table,
/// disambiguating names with a _1, _2, ... suffix. Those cloned rows have conparentid set and
/// cannot be dropped on their own. The table delta read them back as unexpected foreign keys and
/// emitted a DROP for each, so the very first schema apply after a tenant partition appeared blew
/// up -- exactly the "create a tenant, restart the app" sequence in the report.
///
/// The catalog read is Weasel's, so the fix lives there: https://github.com/JasperFx/weasel/pull/389
/// adds `conparentid = 0` to the constraint query. Both tests below pass against a local build of
/// that branch; unskip them when the Weasel dependency picks it up. The other half of #5044 -- the
/// database-wide foreign key guard in NaturalKeyTable -- is Marten's and is covered by
/// EventSourcingTests.Bugs.Bug_5044_natural_key_foreign_key_guard, which runs today.
/// </summary>
public class Bug_5044_natural_key_table_migration: IAsyncLifetime
{
    private const string TenantA = "tenant_a";
    private const string TenantB = "tenant_b";

    private string _schema = null!;

    public async ValueTask InitializeAsync()
    {
        _schema = $"tp_nk5044_{Guid.NewGuid():N}".Substring(0, 28);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try
        {
            await conn.DropSchemaAsync(_schema);
        }
        catch
        {
            // nothing to clean up
        }
    }

    public ValueTask DisposeAsync() => default;

    private DocumentStore BuildStore()
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Projections.Snapshot<CaseStream>(SnapshotLifecycle.Inline);
        });
    }

    [Fact(Skip = "Blocked on JasperFx/weasel#389 -- cloned FK rows for a partitioned referenced table read back as drift")]
    public async Task schema_reapplies_cleanly_after_tenant_partitions_exist()
    {
        // First boot: nothing exists yet.
        await using (var store = BuildStore())
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantA);
        }

        // Second boot against the same database, now that a tenant partition exists on mt_streams.
        // This is where #5044 threw 42P16.
        await using (var store = BuildStore())
        {
            await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        }

        // Adding another tenant clones another set of foreign key rows; a third apply must also be clean.
        await using (var store = BuildStore())
        {
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantB);
            await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        }

        // And the natural key lookup still actually works end to end.
        await using (var store = BuildStore())
        {
            var streamId = Guid.NewGuid();
            await using (var session = store.LightweightSession(TenantA))
            {
                session.Events.StartStream<CaseStream>(streamId, new CaseOpened(streamId, "CASE-001"));
                await session.SaveChangesAsync();
            }

            await using var query = store.LightweightSession(TenantA);
            var found = await query.Events.FetchLatest<CaseStream, CaseNumber>(new CaseNumber("CASE-001"));
            found.ShouldNotBeNull();
            found.Id.ShouldBe(streamId);
        }
    }

    [Fact(Skip = "Blocked on JasperFx/weasel#389 -- cloned FK rows for a partitioned referenced table read back as drift")]
    public async Task migration_is_detected_as_up_to_date_after_tenant_partitions_exist()
    {
        await using (var store = BuildStore())
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantA);
        }

        await using (var store = BuildStore())
        {
            // The cloned foreign key rows must not read back as configuration drift.
            await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }
    }
}

public sealed record CaseNumber(string Value);

public sealed record CaseOpened(Guid CaseId, string Number);

public sealed record CaseRenumbered(Guid CaseId, string Number);

public sealed record CaseStream
{
    public Guid Id { get; set; }

    [NaturalKey]
    public CaseNumber Number { get; set; } = null!;

    [NaturalKeySource]
    public static CaseStream Create(CaseOpened e) =>
        new() { Id = e.CaseId, Number = new CaseNumber(e.Number) };

    [NaturalKeySource]
    public static CaseStream Apply(CaseRenumbered e, CaseStream current) =>
        current with { Number = new CaseNumber(e.Number) };
}
