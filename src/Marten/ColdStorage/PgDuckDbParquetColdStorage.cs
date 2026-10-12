#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;

namespace Marten.ColdStorage;

/// <summary>
///     Cold rows as Parquet files, written and read by the
///     <see href="https://github.com/duckdb/pg_duckdb">pg_duckdb</see> extension
///     (<c>COPY ... TO '...parquet'</c> and <c>read_parquet()</c>).
/// </summary>
/// <remarks>
///     The files are written by the database server, so <see cref="LocationPrefix" /> is a path the
///     server can write to: a local path, or an <c>s3://</c> prefix once a pg_duckdb secret is set up.
///     Local file access is limited to superusers by pg_duckdb's defaults. pg_duckdb refuses to run
///     inside a PL/pgSQL function, so every cold read is a statement Marten sends itself.
/// </remarks>
public sealed class PgDuckDbParquetColdStorage: IColdStorageProvider
{
    public PgDuckDbParquetColdStorage(string locationPrefix)
    {
        if (locationPrefix.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(locationPrefix));
        }

        LocationPrefix = locationPrefix;
    }

    /// <summary>
    ///     Every file is <c>{LocationPrefix}{dataset}_{batch}.parquet</c>.
    /// </summary>
    public string LocationPrefix { get; }

    public IEnumerable<string> InitializationSql(string dataset, IReadOnlyList<ColdColumn> columns)
    {
        // read_parquet() fails on a glob that matches nothing.
        var typedNulls = columns.Select(x => $"NULL::{ColdColumnType(x.PostgresType)} AS {x.Name}").Join(", ");
        yield return ExportSql(dataset, $"SELECT {typedNulls} LIMIT 0", "0");
    }

    public string ColdRelationSql(string dataset, IReadOnlyList<ColdColumn> columns)
    {
        var cold = columns.Select(x => $"r['{x.Name}']::{ColdColumnType(x.PostgresType)} AS {x.Name}").Join(", ");
        return $"SELECT {cold} FROM read_parquet({Quote($"{LocationPrefix}{dataset}_*.parquet")}) r";
    }

    // DuckDB has no jsonb.
    public string ColdColumnType(string postgresType) =>
        postgresType.Equals("jsonb", StringComparison.OrdinalIgnoreCase) ? "json" : postgresType;

    public string ExportSql(string dataset, string selectSql, string batchKey)
    {
        return $"COPY ({selectSql}) TO {Quote($"{LocationPrefix}{dataset}_{batchKey}.parquet")} (FORMAT parquet)";
    }

    private static string Quote(string literal) => $"'{literal.Replace("'", "''")}'";
}
