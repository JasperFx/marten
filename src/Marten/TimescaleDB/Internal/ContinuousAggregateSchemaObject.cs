using System.IO;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.Common;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Marten.TimescaleDB.Internal;

/// <summary>
/// A Weasel schema object that creates a TimescaleDB continuous aggregate (a self-refreshing
/// materialized view) over a hypertable. Ordered after the hypertable it depends on.
/// </summary>
internal sealed class ContinuousAggregateSchemaObject: ISchemaObject
{
    private readonly DbObjectName _hypertable;
    private readonly string _timeColumn;
    private readonly ContinuousAggregateDefinition _definition;

    public ContinuousAggregateSchemaObject(DbObjectName hypertable, string timeColumn,
        ContinuousAggregateDefinition definition)
    {
        _hypertable = hypertable;
        _timeColumn = timeColumn;
        _definition = definition;
    }

    public DbObjectName Identifier => new PostgresqlObjectName(_hypertable.Schema, _definition.ViewName);

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var groupBy = string.IsNullOrWhiteSpace(_definition.GroupByColumns)
            ? "bucket"
            : $"bucket, {_definition.GroupByColumns}";

        var extraSelect = string.IsNullOrWhiteSpace(_definition.GroupByColumns)
            ? string.Empty
            : $"{_definition.GroupByColumns}, ";

        writer.WriteLine(
            $"CREATE MATERIALIZED VIEW IF NOT EXISTS {Identifier.Schema}.{Identifier.Name} WITH (timescaledb.continuous) AS");
        writer.WriteLine(
            $"SELECT time_bucket(INTERVAL '{_definition.BucketInterval}', {_timeColumn}) as bucket, {extraSelect}{_definition.SelectExpressions}");
        writer.WriteLine($"FROM {_hypertable.Schema}.{_hypertable.Name}");
        writer.WriteLine($"GROUP BY {groupBy}");
        writer.WriteLine("WITH NO DATA;");
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        writer.WriteLine($"DROP MATERIALIZED VIEW IF EXISTS {Identifier.Schema}.{Identifier.Name} CASCADE;");
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        builder.Append("select 1 from timescaledb_information.continuous_aggregates where view_schema = ");
        builder.AppendParameter(Identifier.Schema);
        builder.Append(" and view_name = ");
        builder.AppendParameter(Identifier.Name);
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
