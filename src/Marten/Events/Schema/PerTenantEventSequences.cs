#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Marten.Events.Schema;

/// <summary>
/// Per-tenant <c>mt_events_sequence_{suffix}</c> sequences for the
/// <see cref="Marten.Events.IEventStoreOptions.UseTenantPartitionedEvents"/>
/// path (CritterWatch #209, Marten #4596 Phase 1 Session 2).
///
/// <para>
/// Yields one <see cref="Sequence"/> per partition currently registered on the
/// shared <see cref="Marten.Schema.MartenManagedTenantListPartitions"/>. The
/// enumeration is live — Marten's <c>EventGraph._objectsCache</c> holds a
/// reference to <em>this</em> wrapper instance, and each access walks the
/// partition manager's dictionary anew. New tenants joining post-cache-build
/// are picked up automatically on the next schema apply.
/// </para>
///
/// <para>
/// Dynamic tenant joins via <c>AddMartenManagedTenantsAsync</c> bypass the
/// schema-apply path and create their sequences imperatively in the same
/// transaction that adds the partition row; see <c>AdvancedOperations</c>.
/// </para>
/// </summary>
internal class PerTenantEventSequences: ISchemaObject
{
    private readonly EventGraph _events;

    public PerTenantEventSequences(EventGraph events)
    {
        _events = events;
        // No single canonical name — this wrapper expands to N sequences.
        // Use a stable placeholder so identifier-based de-dup in IFeatureSchema
        // works; the real `Sequence` objects each own their own identifier.
        Identifier = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events_sequence_per_tenant");
    }

    public DbObjectName Identifier { get; }

    /// <summary>
    /// The schema-qualified, <b>quoted</b> name of one tenant's event sequence.
    ///
    /// <para>
    /// The partition suffix is the tenant id verbatim (Weasel's
    /// <c>ManagedListPartitions.AddPartitionToAllTables</c> stores
    /// <c>suffix.IsEmpty() ? value.ToLowerInvariant() : suffix</c>), so it can contain characters that are
    /// illegal in an <em>unquoted</em> Postgres identifier — most commonly '-', which every GUID tenant id
    /// has. The name must therefore always be quoted in DDL. See #4924.
    /// </para>
    ///
    /// <para>
    /// Quote, do <b>not</b> sanitize. <c>QuickAppendEventFunction</c> resolves this sequence at append time
    /// as <c>format('%I.%I', schema, 'mt_events_sequence_' || partition_suffix)</c>, reading the raw suffix
    /// straight out of the tenants table. Normalizing the name here (e.g. '-' → '_') would create a sequence
    /// the append function can never find, trading a schema-apply failure for a
    /// <c>42P01 relation does not exist</c> on the first append.
    /// </para>
    /// </summary>
    internal static string QuotedSequenceName(string eventSchema, string partitionSuffix)
        => $"\"{eventSchema}\".\"mt_events_sequence_{partitionSuffix}\"";

    /// <summary>
    /// The partition suffix of every tenant currently registered on the shared partition manager.
    /// `Options.TenantPartitions` is the MartenManagedTenantListPartitions wrapper; its `.Partitions` is the
    /// underlying Weasel ManagedListPartitions; that exposes a `.Partitions`
    /// ReadOnlyDictionary&lt;tenantValue, partitionSuffix&gt;. Triple-`Partitions` is unfortunate but each
    /// level names something different.
    /// </summary>
    private IEnumerable<string> currentPartitionSuffixes()
    {
        var partitions = _events.Options.TenantPartitions?.Partitions.Partitions;
        if (partitions == null) yield break;

        foreach (var pair in partitions)
        {
            yield return pair.Value;
        }
    }

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        // IF NOT EXISTS keeps the statement idempotent — both for the initial
        // schema apply and for the AdvancedOperations.AddMartenManagedTenantsAsync
        // re-apply that fires after a new partition has been registered.
        foreach (var suffix in currentPartitionSuffixes())
        {
            writer.WriteLine(
                $"CREATE SEQUENCE IF NOT EXISTS {QuotedSequenceName(_events.DatabaseSchemaName, suffix)};");
        }
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        foreach (var suffix in currentPartitionSuffixes())
        {
            writer.WriteLine(
                $"DROP SEQUENCE IF EXISTS {QuotedSequenceName(_events.DatabaseSchemaName, suffix)};");
        }
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        // Single query — count of per-tenant sequences in this schema that
        // match our `mt_events_sequence_%` prefix. CreateDeltaAsync compares
        // the count against expected; on mismatch the (additive-only, see
        // CreateSequencesDelta) update path re-emits the IF-NOT-EXISTS create
        // script which is safe to run against an already-partially-applied database.
        var schemaParam = builder.AddParameter(_events.DatabaseSchemaName).ParameterName;
        builder.Append(
            "select count(*) from information_schema.sequences where sequence_schema = :"
            + schemaParam
            + " and sequence_name like 'mt_events_sequence\\_%' escape '\\';");
    }

    public async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        long actualCount = 0;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            actualCount = await reader.GetFieldValueAsync<long>(0, ct).ConfigureAwait(false);
        }

        var expectedCount = currentPartitionSuffixes().Count();

        // The IF-NOT-EXISTS shape means we only ever ADD sequences from the
        // schema-apply path; removal is handled by the partition-drop path.
        // So "actual < expected" → needs update; otherwise no-op.
        var difference = actualCount < expectedCount
            ? SchemaPatchDifference.Update
            : SchemaPatchDifference.None;

        return new CreateSequencesDelta(this, difference);
    }

    /// <summary>
    /// Additive-only update delta. The generic <see cref="SchemaObjectDelta"/> writes updates as
    /// WriteDropStatement + WriteCreateStatement — for this wrapper that would DROP every existing
    /// per-tenant sequence and recreate it at 1 whenever a NEW tenant's sequence is missing,
    /// resetting live tenants' event sequences (seq_id reuse = silent event-store corruption).
    /// A missing sequence only ever needs the IF-NOT-EXISTS creates; existing sequences must
    /// never be touched by an update.
    /// </summary>
    private sealed class CreateSequencesDelta: ISchemaObjectDelta
    {
        public CreateSequencesDelta(PerTenantEventSequences sequences, SchemaPatchDifference difference)
        {
            SchemaObject = sequences;
            Difference = difference;
        }

        public ISchemaObject SchemaObject { get; }
        public SchemaPatchDifference Difference { get; }

        public void WriteUpdate(Migrator rules, TextWriter writer)
        {
            SchemaObject.WriteCreateStatement(rules, writer);
        }

        public void WriteRollback(Migrator rules, TextWriter writer)
        {
        }

        public void WriteRestorationOfPreviousState(Migrator rules, TextWriter writer)
        {
        }
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        foreach (var suffix in currentPartitionSuffixes())
        {
            yield return new PostgresqlObjectName(_events.DatabaseSchemaName, $"mt_events_sequence_{suffix}");
        }
    }

    /// <summary>
    /// Imperative sibling of the schema-apply path: ensures the per-tenant
    /// <c>mt_events_sequence_{suffix}</c> sequence exists for each supplied
    /// partition suffix, in the given database. <c>CREATE SEQUENCE IF NOT EXISTS</c>
    /// keeps it idempotent.
    ///
    /// <para>
    /// Originally inlined inside <c>AdvancedOperations.AddMartenManagedTenantsAsync</c>
    /// for the <c>DefaultTenancy</c> + Marten-managed-partitioning case (#4596
    /// Phase 1 Session 2). Extracted so the sharded runtime-assignment path
    /// (<c>ShardedTenancy.createPartitionsForTenant</c>) can call the same
    /// implementation against the assigned shard database — without this, the
    /// shard's first event append for a newly-provisioned tenant fails with
    /// <c>42P01: relation "{schema}.mt_events_sequence_{suffix}" does not exist</c>
    /// because quick-append calls <c>nextval(...)</c> on the per-tenant sequence.
    /// See #4598.
    /// </para>
    /// </summary>
    /// <param name="database">The database to provision the sequence(s) in. For
    /// sharded tenancy this must be the tenant's assigned shard, NOT the default.</param>
    /// <param name="eventSchema">Schema that hosts <c>mt_events</c> — usually
    /// <c>EventGraph.DatabaseSchemaName</c>.</param>
    /// <param name="partitionSuffixes">Partition suffixes to provision. For the
    /// Marten-managed model the suffix is the tenant id itself.</param>
    public static async Task EnsureSequencesAsync(
        Weasel.Postgresql.PostgresqlDatabase database,
        string eventSchema,
        IEnumerable<string> partitionSuffixes,
        CancellationToken token = default)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        foreach (var suffix in partitionSuffixes)
        {
            var sequenceName = QuotedSequenceName(eventSchema, suffix);
            try
            {
                await conn
                    .CreateCommand($"create sequence if not exists {sequenceName} as bigint;")
                    .ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation
                                                  or PostgresErrorCodes.DuplicateTable
                                                  or PostgresErrorCodes.DuplicateObject)
            {
                // #4596/#4757: CREATE SEQUENCE IF NOT EXISTS is NOT atomic against a concurrent create.
                // Two tasks registering the same tenant (e.g. concurrent AddMartenManagedTenantsAsync)
                // can both pass the IF NOT EXISTS check and race the catalog insert; the loser gets
                // 23505 on pg_class_relname_nsp_index (or 42P07 / 42710). The sequence now exists — which
                // is precisely the idempotent outcome we want — so swallow the concurrent-create race.
                // Each statement runs in its own implicit transaction (no outer transaction here), so the
                // failure does not poison the connection for the remaining suffixes.
            }
        }
        await conn.CloseAsync().ConfigureAwait(false);
    }
}
