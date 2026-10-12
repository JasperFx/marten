#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Marten.ColdStorage;

/// <summary>
///     The provider's initialization (e.g. pg_duckdb's seed file) is not part of this DDL: pg_duckdb
///     cannot execute inside the migration's transaction once it has a savepoint. See
///     <see cref="ColdStorageExtensions.InitializeColdStorageAsync" />.
///     For one hot table <c>t</c>: <c>t_cold</c> (the moved rows) and <c>t_with_cold</c> (hot plus cold).
/// </summary>
internal sealed class ColdViews: ISchemaObject
{
    private readonly Table _table;
    private readonly IColdStorageProvider _provider;
    private readonly string[]? _hotWinsKey;

    /// <param name="hotWinsKey">
    ///     For documents: the key on which a hot row hides every cold row, and only the latest cold
    ///     row of a key is kept. Null for events, which are never both hot and cold.
    /// </param>
    public ColdViews(Table table, IColdStorageProvider provider, string[]? hotWinsKey)
    {
        _table = table;
        _provider = provider;
        _hotWinsKey = hotWinsKey;
        Identifier = WithColdViewFor(table.Identifier);
    }

    public static PostgresqlObjectName ColdViewFor(DbObjectName table) => new(table.Schema, table.Name + "_cold");

    public static PostgresqlObjectName WithColdViewFor(DbObjectName table) => new(table.Schema, table.Name + "_with_cold");

    public static string DatasetFor(DbObjectName table) => $"{table.Schema}_{table.Name}";

    public static IReadOnlyList<ColdColumn> ColumnsOf(Table table) =>
        table.Columns.Select(x => new ColdColumn(x.Name, x.Type)).ToList();

    public DbObjectName Identifier { get; }

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var columns = ColumnsOf(_table);
        var dataset = DatasetFor(_table.Identifier);

        var hot = _table.Identifier.QualifiedName;
        var cold = ColdViewFor(_table.Identifier).QualifiedName;
        writer.WriteLine($"CREATE OR REPLACE VIEW {cold} AS {_provider.ColdRelationSql(dataset, columns)};");

        var hotColumns = columns.Select(x =>
        {
            var coldType = _provider.ColdColumnType(x.PostgresType);
            return coldType == x.PostgresType ? x.Name : $"{x.Name}::{coldType} AS {x.Name}";
        }).Join(", ");

        string coldSource;
        if (_hotWinsKey == null)
        {
            coldSource = $"SELECT {columns.Select(x => x.Name).Join(", ")} FROM {cold}";
        }
        else
        {
            // Anti-joins rather than a window, so a filter on the view still reaches the cold scan.
            var shadowedByHot = _hotWinsKey.Select(x => $"h.{x} = c.{x}").Join(" AND ");
            var shadowedByNewer = _hotWinsKey.Select(x => $"n.{x} = c.{x}").Join(" AND ");
            coldSource =
                $"SELECT {columns.Select(x => "c." + x.Name).Join(", ")} FROM {cold} c " +
                $"WHERE NOT EXISTS (SELECT 1 FROM {hot} h WHERE {shadowedByHot}) " +
                $"AND NOT EXISTS (SELECT 1 FROM {cold} n WHERE {shadowedByNewer} AND n.mt_last_modified > c.mt_last_modified)";
        }

        writer.WriteLine($"CREATE OR REPLACE VIEW {Identifier.QualifiedName} AS SELECT {hotColumns} FROM {hot} UNION ALL {coldSource};");
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        writer.WriteLine($"DROP VIEW IF EXISTS {Identifier.QualifiedName};");
        writer.WriteLine($"DROP VIEW IF EXISTS {ColdViewFor(_table.Identifier).QualifiedName};");
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        builder.Append("select 1 from pg_views where schemaname = ");
        builder.AppendParameter(Identifier.Schema);
        builder.Append(" and viewname = ");
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
        yield return ColdViewFor(_table.Identifier);
    }
}
