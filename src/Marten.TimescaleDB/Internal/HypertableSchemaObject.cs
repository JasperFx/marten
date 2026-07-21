using System.Data.Common;
using System.Globalization;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Marten.TimescaleDB.Internal;

/// <summary>
/// A Weasel schema object that converts an already-created Marten table into a TimescaleDB
/// hypertable and, on first creation, applies the configured compression and retention policies.
///
/// This object is intentionally ordered AFTER the target table in Marten's schema application
/// (it lives in a custom, non-Marten IFeatureSchema which Marten yields last), so by the time
/// its create statement runs the underlying table already exists and can be migrated in place.
/// </summary>
internal sealed class HypertableSchemaObject: ISchemaObject
{
    private readonly DbObjectName _table;
    private readonly HypertableOptions _options;

    public HypertableSchemaObject(DbObjectName table, HypertableOptions options)
    {
        _table = table;
        _options = options;
    }

    // A synthetic identifier distinct from the table's own identifier so Weasel never confuses
    // this marker object with the real table definition.
    public DbObjectName Identifier => new PostgresqlObjectName(_table.Schema, $"hypertable::{_table.Name}");

    private string QualifiedName => $"{_table.Schema}.{_table.Name}";

    private static string Interval(TimeSpan span) =>
        $"INTERVAL '{((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds'";

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var chunk = _options.ChunkInterval.HasValue
            ? $", chunk_time_interval => {Interval(_options.ChunkInterval.Value)}"
            : string.Empty;

        // if_not_exists + migrate_data keep this safe to run against a populated table and safe
        // to re-run — create_hypertable is a no-op once the relation is already a hypertable.
        // create_default_indexes => FALSE keeps Marten's own schema model authoritative: without
        // it TimescaleDB adds an index on the time column that Marten's schema-diff would then try
        // to drop on every migration. Add any time-column index explicitly on the projection/document
        // table if you want one.
        writer.WriteLine(
            $"SELECT create_hypertable('{QualifiedName}', '{_options.TimeColumn}'{chunk}, if_not_exists => TRUE, migrate_data => TRUE, create_default_indexes => FALSE);");

        if (_options.CompressAfter.HasValue)
        {
            var orderBy = string.IsNullOrWhiteSpace(_options.CompressOrderBy)
                ? $"{_options.TimeColumn} DESC"
                : _options.CompressOrderBy!;

            var compressSettings = $"timescaledb.compress, timescaledb.compress_orderby = '{orderBy}'";
            if (!string.IsNullOrWhiteSpace(_options.CompressSegmentBy))
            {
                compressSettings += $", timescaledb.compress_segmentby = '{_options.CompressSegmentBy}'";
            }

            writer.WriteLine($"ALTER TABLE {QualifiedName} SET ({compressSettings});");
            writer.WriteLine(
                $"SELECT add_compression_policy('{QualifiedName}', {Interval(_options.CompressAfter.Value)}, if_not_exists => TRUE);");
        }

        if (_options.RetainFor.HasValue)
        {
            writer.WriteLine(
                $"SELECT add_retention_policy('{QualifiedName}', {Interval(_options.RetainFor.Value)}, if_not_exists => TRUE);");
        }
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        // Reverting a hypertable to a plain table is not directly supported by TimescaleDB, but we
        // can at least remove the policies we added so a re-provision starts from a clean slate.
        if (_options.RetainFor.HasValue)
        {
            writer.WriteLine($"SELECT remove_retention_policy('{QualifiedName}', if_not_exists => TRUE);");
        }

        if (_options.CompressAfter.HasValue)
        {
            writer.WriteLine($"SELECT remove_compression_policy('{QualifiedName}', if_not_exists => TRUE);");
        }
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        builder.Append("select 1 from timescaledb_information.hypertables where hypertable_schema = ");
        builder.AppendParameter(_table.Schema);
        builder.Append(" and hypertable_name = ");
        builder.AppendParameter(_table.Name);
        builder.Append(";");
    }

    public async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        var exists = await reader.ReadAsync(ct).ConfigureAwait(false);
        return new SchemaObjectDelta(this, exists ? SchemaPatchDifference.None : SchemaPatchDifference.Create);
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        yield return Identifier;
    }
}
