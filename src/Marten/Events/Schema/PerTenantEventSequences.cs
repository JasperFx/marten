#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    private IEnumerable<Sequence> currentSequences()
    {
        // `Options.TenantPartitions` is the MartenManagedTenantListPartitions wrapper;
        // its `.Partitions` is the underlying Weasel ManagedListPartitions; that
        // exposes a `.Partitions` ReadOnlyDictionary<tenantValue, partitionSuffix>.
        // Triple-`Partitions` is unfortunate but each level names something different.
        var partitions = _events.Options.TenantPartitions?.Partitions.Partitions;
        if (partitions == null) yield break;

        foreach (var pair in partitions)
        {
            yield return new Sequence(new PostgresqlObjectName(
                _events.DatabaseSchemaName, $"mt_events_sequence_{pair.Value}"));
        }
    }

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        // IF NOT EXISTS keeps the statement idempotent — both for the initial
        // schema apply and for the AdvancedOperations.AddMartenManagedTenantsAsync
        // re-apply that fires after a new partition has been registered.
        foreach (var seq in currentSequences())
        {
            writer.WriteLine($"CREATE SEQUENCE IF NOT EXISTS {seq.Identifier};");
        }
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        foreach (var seq in currentSequences())
        {
            writer.WriteLine($"DROP SEQUENCE IF EXISTS {seq.Identifier};");
        }
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        // Single query — count of per-tenant sequences in this schema that
        // match our `mt_events_sequence_%` prefix. CreateDeltaAsync compares
        // the count against expected; on mismatch the WriteUpdate path
        // re-emits the IF-NOT-EXISTS create script which is safe to run
        // against an already-partially-applied database.
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

        var expectedCount = currentSequences().Count();

        // The IF-NOT-EXISTS shape means we only ever ADD sequences from the
        // schema-apply path; removal is handled by the partition-drop path.
        // So "actual < expected" → needs update; otherwise no-op.
        var difference = actualCount < expectedCount
            ? SchemaPatchDifference.Update
            : SchemaPatchDifference.None;

        return new SchemaObjectDelta(this, difference);
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        foreach (var seq in currentSequences())
        {
            yield return seq.Identifier;
        }
    }
}
