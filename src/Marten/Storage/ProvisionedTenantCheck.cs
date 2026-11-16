using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using JasperFx.Core;
using Marten.Events.Schema;
using Marten.Schema;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Storage;

/// <summary>
/// #5986: a synchronous answer to "would <c>ShardedTenancy.createPartitionsForTenant</c> change anything for this
/// tenant on this shard?", so the synchronous <c>GetTenant</c> can skip the #4942 first-sight repair for a tenant
/// that is already fully provisioned, without blocking a thread on the asynchronous repair.
/// </summary>
/// <remarks>
/// <para>
/// The check is derived from what the repair does, step by step, and answers "provisioned" only when every step
/// would be a no-op. The repair is <c>DatabaseScopedTenantPartitions.AddPartitionToAllTables</c> with
/// <c>{ tenantId: tenantId }</c>, followed by <c>PerTenantEventSequences.EnsureSequencesAsync</c>:
/// </para>
/// <list type="number">
/// <item>Migrate the shard's <c>mt_tenant_partitions</c> registry table. A no-op only when the table has exactly
/// the shape Weasel creates, which is what <see cref="RegistryShapeSql"/> accepts.</item>
/// <item>Upsert the registry row <c>(tenantId, tenantId)</c>. A no-op only when that row is already there with
/// exactly that suffix.</item>
/// <item>For every table this store registers with this partition manager, create or widen the partition
/// <c>{table}_{sanitized suffix}</c> so its bound covers the whole bucket. A no-op when the partition exists,
/// is attached and its bound already covers every value, read exactly the way Weasel reads it. When the parent
/// table does not exist on the shard the repair's CREATE fails and is logged, which changes nothing either.</item>
/// <item>The #4944 sweep: <c>CREATE TABLE IF NOT EXISTS</c> of the partition on every tenant-partitioned parent
/// in the catalog that the store does not register. A no-op when a relation of that name already exists.</item>
/// <item>Under <c>UseTenantPartitionedEvents</c>, <c>CREATE SEQUENCE IF NOT EXISTS</c> for the tenant's event
/// sequence. A no-op when a relation of that name already exists.</item>
/// </list>
/// <para>
/// Wherever the repair's own decision cannot be reproduced exactly, the check errs towards "not provisioned",
/// because the fallback is the repair itself: a stricter check costs one sync-over-async repair, a looser one
/// would skip a repair that was needed.
/// </para>
/// </remarks>
internal static class ProvisionedTenantCheck
{
    // Postgres silently truncates identifiers longer than this many bytes (NAMEDATALEN - 1)
    private const int MaxIdentifierBytes = 63;

    /// <summary>
    /// True when the tenant's registry row, partitions and event sequence are already exactly what the repair would
    /// leave behind. On true, this database's in-memory partition view is updated the way the repair updates it.
    /// </summary>
    /// <remarks>
    /// Synchronous I/O on <paramref name="database"/>, two single-statement round trips. The caller must have ruled
    /// out a multiplexed data source, on which Npgsql refuses synchronous I/O.
    /// </remarks>
    public static bool IsProvisioned(StoreOptions options, MartenDatabase database, string tenantId)
    {
        var tenantPartitions = options.TenantPartitions;

        // createPartitionsForTenant returns immediately without managed partitioning, so there is nothing to repair
        if (tenantPartitions == null) return true;

        var partitions = tenantPartitions.Partitions;
        var suffix = sanitizeSuffix(tenantId);

        // The repair enumerates the store's tables through AllObjects() and AllSchemaNames(). Both go through
        // MartenDatabase.BuildFeatureSchemas(), which stamps the ambient partition scope; on a synchronous caller
        // that write would outlive this method, so put the scope back.
        ISchemaObject[] objects;
        var priorScope = TenantPartitionDatabaseScope.Current;
        try
        {
            objects = database.AllObjects().ToArray();
        }
        finally
        {
            TenantPartitionDatabaseScope.Current = priorScope;
        }

        var tables = objects.OfType<Table>().ToArray();

        // ManagedListPartitions.additivelyMigrateTablesForNewPartitions: the tables this manager owns
        var managed = tables
            .Where(x => x.Partitioning is ListPartitioning list && ReferenceEquals(list.PartitionManager, partitions))
            .ToArray();

        // The sweep skips every table the base pass reported on (all of the managed ones) and every table the store
        // marks as list partitioned by some other manager: together, every registered list-partitioned table
        var handled = tables
            .Where(x => x.Partitioning is ListPartitioning)
            .Select(x => x.Identifier.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var schemas = objects.Select(x => x.Identifier.Schema).Distinct().ToArray();
        var sweep = partitions.SweepPartitionedTablesFromDatabase && schemas.Length > 0;

        using var conn = database.CreateConnection();
        conn.Open();

        var registry = readRegistry(conn, tenantPartitions.TenantsTableName);

        // Step 2, the registry upsert of (tenantId, tenantId)
        if (!registry.TryGetValue(tenantId, out var storedSuffix) || !string.Equals(storedSuffix, tenantId, StringComparison.Ordinal))
        {
            return false;
        }

        var desired = desiredBoundValues(partitions, registry, tenantId, suffix);

        var sequence = options.Events.UseTenantPartitionedEvents
            ? PerTenantEventSequences.QuotedSequenceName(options.Events.DatabaseSchemaName, tenantId)
            : null;

        var facts = readFacts(conn, tenantPartitions.TenantsTableName, managed, suffix, sequence, sweep ? schemas : null);

        // Step 1, the registry table's own migration
        if (!facts.RegistryHasExpectedShape) return false;

        // Step 3, the managed tables
        foreach (var table in managed)
        {
            var schema = table.Identifier.Schema;
            var partitionName = table.Identifier.Name + "_" + suffix;

            if (facts.Partitions.TryGetValue((schema, partitionName), out var bound))
            {
                if (!boundCovers(bound, desired)) return false;
                continue;
            }

            // No partition. The repair's CREATE ... PARTITION OF fails only when the parent is missing too. An exact
            // catalog lookup cannot see a parent whose name Postgres truncated, so a long name counts as present
            if (facts.Parents.Contains((schema, table.Identifier.Name))
                || exceedsIdentifierLimit(schema) || exceedsIdentifierLimit(table.Identifier.Name))
            {
                return false;
            }
        }

        // Step 4, the sweep over tables the store does not register
        foreach (var (schema, name, partitionExists) in facts.SweptTables)
        {
            if (handled.Contains(new DbObjectName(schema, name).QualifiedName)) continue;
            if (!partitionExists) return false;
        }

        // Step 5, the per-tenant event sequence
        if (sequence != null && !facts.SequenceExists) return false;

        // Nothing to repair. Leave the same in-memory state the repair leaves: this database's view is a fresh read of
        // its registry, with the tenant merged in
        var hydrated = registry.ToDictionary(x => x.Key, x => (x.Value ?? x.Key).ToLowerInvariant());
        partitions.PublishHydratedView(database, hydrated, new Dictionary<string, string> { [tenantId] = tenantId });

        return true;
    }

    /// <summary>
    /// The registry exactly as <c>DatabaseScopedTenantPartitions.HydrateAsync</c> reads it, with the raw suffix.
    /// </summary>
    private static Dictionary<string, string?> readRegistry(NpgsqlConnection conn, DbObjectName registryTable)
    {
        var registry = new Dictionary<string, string?>();

        using var cmd = conn.CreateCommand($"select partition_value, partition_suffix from {registryTable.QualifiedName}");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            registry[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return registry;
    }

    /// <summary>
    /// The values the repair would require in the tenant's partition bound. <c>resolveBuckets</c> takes every value
    /// of the store-wide map in the same bucket, after <c>InitializeAsync</c> may have loaded that map from this
    /// shard's registry. Taking both sources is a superset of either, which can only make the check stricter.
    /// </summary>
    private static string[] desiredBoundValues(DatabaseScopedTenantPartitions partitions,
        Dictionary<string, string?> registry, string tenantId, string suffix)
    {
        var values = new HashSet<string>(StringComparer.Ordinal) { tenantId };

        // The base class's store-wide snapshot, not the database-scoped view that hides it
        foreach (var pair in ((ManagedListPartitions)partitions).Partitions)
        {
            if (sanitizeSuffix(pair.Value) == suffix) values.Add(pair.Key);
        }

        foreach (var pair in registry)
        {
            if (sanitizeSuffix(pair.Value ?? pair.Key) == suffix) values.Add(pair.Key);
        }

        return values.Select(x => x.FormatSqlValue()).ToArray();
    }

    /// <summary>
    /// <c>ManagedListPartitions.createOrWidenPartitionAsync</c>'s no-op condition: the partition is attached and
    /// every desired value is in its bound, parsed exactly the way <c>fetchPartitionBoundAsync</c> parses it.
    /// </summary>
    private static bool boundCovers(string? expression, string[] desired)
    {
        // Not attached to a parent: the repair would ATTACH it
        if (expression == null) return false;

        HashSet<string> covered;
        try
        {
            covered = getStringWithinParentheses(expression)
                .ToDelimitedArray(',')
                .Select(normalizePartitionValue)
                .ToHashSet();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Weasel throws on the same expression, and the repair records that table as failed. Let it.
            return false;
        }

        return desired.All(x => covered.Contains(normalizePartitionValue(x)));
    }

    private static Facts readFacts(NpgsqlConnection conn, DbObjectName registryTable, Table[] managed,
        string suffix, string? sequence, string[]? sweepSchemas)
    {
        var sql = new StringBuilder();

        sql.Append("select 'shape'::text, null::text, null::text, (").Append(RegistryShapeSql).AppendLine(")::text");

        // fetchPartitionBoundAsync's lookup, for every managed table at once
        sql.AppendLine("""
                       union all
                       select 'partition', n.nspname::text, c.relname::text, pg_get_expr(c.relpartbound, c.oid)
                       from pg_catalog.pg_class c
                       join pg_catalog.pg_namespace n on n.oid = c.relnamespace
                       join unnest(:partition_schemas, :partition_names) as p(schema_name, relation_name)
                         on n.nspname = p.schema_name and c.relname = p.relation_name
                       union all
                       select 'parent', n.nspname::text, c.relname::text, null
                       from pg_catalog.pg_class c
                       join pg_catalog.pg_namespace n on n.oid = c.relnamespace
                       join unnest(:parent_schemas, :parent_names) as p(schema_name, relation_name)
                         on n.nspname = p.schema_name and c.relname = p.relation_name
                       """);

        if (sequence != null)
        {
            // to_regclass parses the name exactly as CREATE SEQUENCE IF NOT EXISTS does, quoting and truncation included
            sql.AppendLine("union all select 'sequence', null, null, (to_regclass(:sequence) is not null)::text");
        }

        if (sweepSchemas != null)
        {
            sql.Append("union all select 'swept', d.nspname::text, d.relname::text, (exists (")
                .Append("select 1 from pg_catalog.pg_class x join pg_catalog.pg_namespace xn on xn.oid = x.relnamespace ")
                .Append("where xn.nspname = d.nspname and x.relname = d.relname || '_' || :suffix))::text from (")
                .Append(DatabaseScopedTenantPartitions.TenantPartitionedParentsSql)
                .AppendLine(") d");
        }

        using var cmd = conn.CreateCommand(sql.ToString());
        cmd.With("registry_schema", registryTable.Schema);
        cmd.With("registry_name", registryTable.Name);
        cmd.With("registry_pk", $"pkey_{registryTable.Name}_partition_value");
        cmd.With("partition_schemas", managed.Select(x => x.Identifier.Schema).ToArray());
        cmd.With("partition_names", managed.Select(x => x.Identifier.Name + "_" + suffix).ToArray());
        cmd.With("parent_schemas", managed.Select(x => x.Identifier.Schema).ToArray());
        cmd.With("parent_names", managed.Select(x => x.Identifier.Name).ToArray());

        if (sequence != null)
        {
            cmd.With("sequence", sequence);
        }

        if (sweepSchemas != null)
        {
            cmd.With("schemas", sweepSchemas);
            cmd.With("tenantColumn", TenantIdColumn.Name);
            cmd.With("suffix", suffix);
        }

        var facts = new Facts();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var schema = reader.IsDBNull(1) ? null : reader.GetString(1);
            var name = reader.IsDBNull(2) ? null : reader.GetString(2);
            var detail = reader.IsDBNull(3) ? null : reader.GetString(3);

            switch (reader.GetString(0))
            {
                case "shape":
                    facts.RegistryHasExpectedShape = detail == "true";
                    break;
                case "partition":
                    facts.Partitions[(schema!, name!)] = detail;
                    break;
                case "parent":
                    facts.Parents.Add((schema!, name!));
                    break;
                case "sequence":
                    facts.SequenceExists = detail == "true";
                    break;
                case "swept":
                    facts.SweptTables.Add((schema!, name!, detail == "true"));
                    break;
            }
        }

        return facts;
    }

    /// <summary>
    /// True only when <c>mt_tenant_partitions</c> is exactly the table Weasel creates for the registry: the two
    /// <c>varchar</c> columns, the primary key on <c>partition_value</c> with Weasel's name, and nothing else Weasel's
    /// table delta compares (no other column, index, foreign key or check constraint, and not partitioned). Anything
    /// else counts as a pending migration, so the repair runs it.
    /// </summary>
    internal const string RegistryShapeSql =
        """
        (select count(*) from information_schema.columns
          where table_schema = :registry_schema and table_name = :registry_name) = 2
        and exists (select 1 from information_schema.columns
          where table_schema = :registry_schema and table_name = :registry_name
            and column_name = 'partition_value' and data_type = 'character varying'
            and character_maximum_length is null and column_default is null
            and is_nullable = 'NO' and is_generated = 'NEVER')
        and exists (select 1 from information_schema.columns
          where table_schema = :registry_schema and table_name = :registry_name
            and column_name = 'partition_suffix' and data_type = 'character varying'
            and character_maximum_length is null and column_default is null
            and is_nullable = 'YES' and is_generated = 'NEVER')
        and exists (select 1 from pg_catalog.pg_class t
          join pg_catalog.pg_namespace n on n.oid = t.relnamespace
          where n.nspname = :registry_schema and t.relname = :registry_name and t.relkind = 'r')
        and (select count(*) from pg_catalog.pg_index x
          join pg_catalog.pg_class t on t.oid = x.indrelid
          join pg_catalog.pg_namespace n on n.oid = t.relnamespace
          where n.nspname = :registry_schema and t.relname = :registry_name) = 1
        and exists (select 1 from pg_catalog.pg_index x
          join pg_catalog.pg_class i on i.oid = x.indexrelid
          join pg_catalog.pg_class t on t.oid = x.indrelid
          join pg_catalog.pg_namespace n on n.oid = t.relnamespace
          join pg_catalog.pg_am am on am.oid = i.relam
          join pg_catalog.pg_attribute a on a.attrelid = t.oid and a.attnum = x.indkey[0]
          where n.nspname = :registry_schema and t.relname = :registry_name
            and x.indisprimary and x.indisvalid and x.indnatts = 1
            and x.indexprs is null and x.indpred is null and am.amname = 'btree'
            and a.attname = 'partition_value' and lower(i.relname) = lower(:registry_pk))
        and not exists (select 1 from pg_catalog.pg_constraint k
          join pg_catalog.pg_class t on t.oid = k.conrelid
          join pg_catalog.pg_namespace n on n.oid = t.relnamespace
          where n.nspname = :registry_schema and t.relname = :registry_name and k.contype in ('f', 'c'))
        """;

    private static bool exceedsIdentifierLimit(string identifier)
        => Encoding.UTF8.GetByteCount(identifier) > MaxIdentifierBytes;

    /// <summary>
    /// Weasel's <c>ListPartition.SanitizeSuffix</c> (internal there): lower-case, and anything outside
    /// <c>[a-z0-9_]</c> becomes '_'. The partition table is named with this.
    /// </summary>
    private static string sanitizeSuffix(string suffix)
    {
        var lower = suffix.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            builder.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Weasel's <c>PartitionExtensions.GetStringWithinParantheses</c> (internal there).
    /// </summary>
    private static string getStringWithinParentheses(string raw)
    {
        var start = raw.IndexOf('(');
        var end = raw.IndexOf(')');
        return raw.Substring(start + 1, end - start - 1);
    }

    /// <summary>
    /// Weasel's <c>PartitionExtensions.NormalizePartitionValue</c> (internal there). Both sides of the comparison go
    /// through it, as they do in <c>createOrWidenPartitionAsync</c>.
    /// </summary>
    private static string normalizePartitionValue(string? raw)
    {
        if (raw is null) return string.Empty;

        var value = raw.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            value = value.Substring(1, value.Length - 2);
        }

        if (looksLikeTimestamp(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
        {
            return moment.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static bool looksLikeTimestamp(string value)
    {
        return value.Length >= 8
               && value.IndexOf('-') >= 4
               && char.IsDigit(value[0]) && char.IsDigit(value[1])
               && char.IsDigit(value[2]) && char.IsDigit(value[3]);
    }

    private sealed class Facts
    {
        public bool RegistryHasExpectedShape;
        public bool SequenceExists;
        public readonly Dictionary<(string Schema, string Name), string?> Partitions = new();
        public readonly HashSet<(string Schema, string Name)> Parents = new();
        public readonly List<(string Schema, string Name, bool PartitionExists)> SweptTables = new();
    }
}
