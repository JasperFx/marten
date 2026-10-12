#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;

namespace Marten.Events.Archiving.ObjectStorage;

/// <summary>
///     Archived events as Parquet files, written and read by the
///     <see href="https://github.com/duckdb/pg_duckdb">pg_duckdb</see> extension
///     (<c>COPY ... TO '...parquet'</c> and <c>read_parquet()</c>).
/// </summary>
/// <remarks>
///     The files are written by the database server, so <see cref="LocationPrefix" /> is a path the
///     server can write to: a local path, or an <c>s3://</c> prefix once a pg_duckdb secret is set up.
///     Local file access is limited to superusers by pg_duckdb's defaults.
/// </remarks>
public sealed class PgDuckDbParquetArchive: IArchivedEventObjectStorage
{
    public PgDuckDbParquetArchive(string locationPrefix)
    {
        if (locationPrefix.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(locationPrefix));
        }

        LocationPrefix = locationPrefix;
    }

    /// <summary>
    ///     Every file is <c>{LocationPrefix}{batch}.parquet</c>, and the view reads <c>{LocationPrefix}*.parquet</c>.
    /// </summary>
    public string LocationPrefix { get; }

    public IEnumerable<string> InitializationSql(IReadOnlyList<ArchivedEventColumn> columns)
    {
        // read_parquet() fails on a glob that matches nothing, so the view needs one file from the start.
        var typedNulls = columns.Select(x => $"NULL::{DuckDbType(x.PostgresType)} AS {x.Name}").Join(", ");
        yield return ExportSql($"SELECT {typedNulls} LIMIT 0", "0");
    }

    public string ArchiveInclusiveViewSql(string eventsTable, IReadOnlyList<ArchivedEventColumn> columns)
    {
        // DuckDB has no jsonb, and a UNION needs one type per column, so both sides read json.
        var hot = columns.Select(x => IsJsonb(x) ? $"{x.Name}::json AS {x.Name}" : x.Name).Join(", ");
        var cold = columns.Select(x => $"r['{x.Name}']::{DuckDbType(x.PostgresType)} AS {x.Name}").Join(", ");

        return $"SELECT {hot} FROM {eventsTable} UNION ALL SELECT {cold} FROM read_parquet({Quote(LocationPrefix + "*.parquet")}) r";
    }

    public string ExportSql(string selectSql, string batchKey)
    {
        return $"COPY ({selectSql}) TO {Quote(LocationPrefix + batchKey + ".parquet")} (FORMAT parquet)";
    }

    private static bool IsJsonb(ArchivedEventColumn column) =>
        column.PostgresType.Equals("jsonb", StringComparison.OrdinalIgnoreCase);

    private static string DuckDbType(string postgresType) =>
        postgresType.Equals("jsonb", StringComparison.OrdinalIgnoreCase) ? "json" : postgresType;

    private static string Quote(string literal) => $"'{literal.Replace("'", "''")}'";
}
