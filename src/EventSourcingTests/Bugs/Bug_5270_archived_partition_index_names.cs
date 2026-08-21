using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5270, a follow-up to #3520. <c>EventStreamUnexpectedMaxEventIdExceptionTransform</c> recognised the
/// unique index that guards one version per stream by an enumerated list of names, and the
/// conjoined-tenancy variant was missing — so <c>TenancyStyle.Conjoined</c> together with
/// <c>UseArchivedStreamPartitioning</c> surfaced a lost append race as a raw <c>PostgresException</c>
/// rather than an <see cref="EventStreamUnexpectedMaxEventIdException" />.
/// <para>
/// Two kinds of test here, deliberately. The first group pins the transform itself and is fully
/// deterministic. The second reads the index names PostgreSQL <i>actually</i> generates for each
/// configuration and feeds them through the same predicate — that is the half that catches the next
/// variant, because the bug was never in the transform's logic, it was in the list of names being
/// incomplete.
/// </para>
/// </summary>
public class Bug_5270_archived_partition_index_names: OneOffConfigurationsContext
{
    private static Exception UniqueViolation(string constraintName) =>
        new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation, constraintName: constraintName, tableName: "mt_events");

    private static bool IsTransformed(Exception e) =>
        new Marten.Services.EventStreamUnexpectedMaxEventIdExceptionTransform()
            .TryTransform(e, out var transformed) && transformed is EventStreamUnexpectedMaxEventIdException;

    public static TheoryData<string> VersionGuardIndexNames => new()
    {
        // no partitioning — the index as Marten names it
        "pk_mt_events_stream_and_version",
        // UseArchivedStreamPartitioning, single tenant (the name #3520 added)
        "mt_events_default_stream_id_version_is_archived_idx",
        // UseArchivedStreamPartitioning + TenancyStyle.Conjoined — the one that was missing
        "mt_events_default_tenant_id_stream_id_version_is_archived_idx",
        // and the archived partition, which no enumerated list ever included either
        "mt_events_archived_tenant_id_stream_id_version_is_archived_idx"
    };

    [Theory]
    [MemberData(nameof(VersionGuardIndexNames))]
    public void a_violation_of_the_version_guard_is_transformed(string constraintName) =>
        IsTransformed(UniqueViolation(constraintName)).ShouldBeTrue();

    [Fact]
    public void the_other_unique_index_on_mt_events_is_left_alone()
    {
        // mt_events also carries a unique index over id. A duplicate event id is not an
        // optimistic-concurrency conflict and must keep surfacing as itself.
        IsTransformed(UniqueViolation("mt_events_default_id_idx")).ShouldBeFalse();
        IsTransformed(UniqueViolation("pk_mt_events")).ShouldBeFalse();
    }

    [Fact]
    public void unrelated_failures_are_left_alone()
    {
        IsTransformed(UniqueViolation("pk_mt_doc_user")).ShouldBeFalse();
        IsTransformed(UniqueViolation("mt_doc_user_stream_id_version_idx")).ShouldBeFalse();

        // Right constraint, wrong SQLSTATE.
        IsTransformed(new PostgresException("nope", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation,
            constraintName: "pk_mt_events_stream_and_version")).ShouldBeFalse();

        IsTransformed(new Exception("not a postgres exception")).ShouldBeFalse();

        // No constraint name at all.
        IsTransformed(new PostgresException("nope", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation))
            .ShouldBeFalse();
    }

    public static TheoryData<TenancyStyle, bool> Configurations => new()
    {
        { TenancyStyle.Single, false },
        { TenancyStyle.Single, true },
        { TenancyStyle.Conjoined, false },
        { TenancyStyle.Conjoined, true }
    };

    /// <summary>
    /// The important one. Builds each supported configuration, reads the unique indexes PostgreSQL
    /// really created over (stream_id, version) — including the partition children — and asserts the
    /// transform recognises every one of them. An enumerated list cannot survive this test; the shape
    /// match can.
    /// </summary>
    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task every_real_version_guard_index_is_recognised(TenancyStyle tenancy, bool archivedPartitioning)
    {
        // A schema per configuration. The four cases would otherwise take turns dropping and rebuilding
        // one shared schema, which makes the catalog read depend on theory ordering.
        _schemaName = $"bug5270_{tenancy}_{(archivedPartitioning ? "part" : "flat")}".ToLowerInvariant();

        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = tenancy;
            opts.Events.UseArchivedStreamPartitioning = archivedPartitioning;
            opts.Events.AddEventType<QuestStarted>();

            if (tenancy == TenancyStyle.Conjoined)
            {
                opts.Policies.AllDocumentsAreMultiTenanted();
            }
        });

        // Append rather than only applying changes: the event store's tables are not created at all
        // unless the event graph is active, and an empty apply leaves nothing to read.
        await using (var session = tenancy == TenancyStyle.Conjoined
                         ? theStore.LightweightSession("acme")
                         : theStore.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new QuestStarted { Name = "Find the Horn" });
            await session.SaveChangesAsync();
        }

        var names = await VersionGuardIndexesAsync();

        // Guard the guard: if this comes back empty the assertion below is vacuous.
        names.ShouldNotBeEmpty();

        foreach (var name in names)
        {
            IsTransformed(UniqueViolation(name))
                .ShouldBeTrue($"'{name}' is a real unique index over (stream_id, version) for " +
                              $"{tenancy}/{(archivedPartitioning ? "partitioned" : "not partitioned")} " +
                              "and the transform does not recognise it");
        }
    }

    /// <summary>
    /// Every unique index in this schema whose definition covers both stream_id and version — the parent
    /// and, when the table is partitioned, its children. Read from the catalog rather than assumed,
    /// because assuming the names is the defect.
    /// </summary>
    private async Task<IReadOnlyList<string>> VersionGuardIndexesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          select indexname from pg_indexes
                          where schemaname = @schema
                            and tablename like 'mt_events%'
                            and indexdef like '%UNIQUE%'
                            and indexdef like '%stream_id%'
                            and indexdef like '%version%'
                          order by indexname
                          """;
        cmd.Parameters.AddWithValue("schema", SchemaName);

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        return names;
    }
}
