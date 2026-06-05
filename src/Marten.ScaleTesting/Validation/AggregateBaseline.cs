using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Marten.ScaleTesting.Validation;

/// <summary>
/// Per-projection-table aggregate baseline: row count + SHA-256 hash of the
/// `data` jsonb column contents sorted by id. Two runs that produce
/// byte-identical aggregates yield byte-identical hashes — that's the
/// correctness gate for the `validate` / `stress` subcommands.
///
/// <para>
/// Why hash the jsonb directly (rather than reflect on the projection types):
/// keeps the baseline mechanism dumb. Any projection added to the composite
/// gets covered automatically as long as its table lives under the configured
/// schema. The trade-off is that the baseline is sensitive to serializer
/// version drift / json key ordering changes — we accept that for the
/// run-vs-run determinism signal, which is the only thing the harness needs.
/// </para>
/// </summary>
public sealed record TableSnapshot(string Table, long RowCount, string DataHash);

/// <summary>
/// Top-level baseline / current snapshot envelope. JSON-serialized for both
/// the on-disk baseline file and the `validate` subcommand's run output.
/// </summary>
public sealed record AggregateBaseline(string SchemaName, DateTimeOffset CapturedAt, IReadOnlyList<TableSnapshot> Tables);

internal static class AggregateBaselineCapture
{
    /// <summary>
    /// SHA-256 over `id || '\0' || data::text || '\n'` rows ordered by `id::text`.
    /// One pass per table; uses a streaming hash to keep memory flat even for
    /// 20M-event-derived projection tables. Returns the aggregate snapshot for
    /// every <c>mt_doc_*</c> table in the schema.
    /// </summary>
    public static async Task<AggregateBaseline> CaptureAsync(
        string connectionString, string schemaName, CancellationToken token = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(token).ConfigureAwait(false);

        var tables = await listProjectionTablesAsync(conn, schemaName, token).ConfigureAwait(false);

        var snapshots = new List<TableSnapshot>(tables.Count);
        foreach (var table in tables.OrderBy(x => x, StringComparer.Ordinal))
        {
            snapshots.Add(await hashTableAsync(conn, schemaName, table, token).ConfigureAwait(false));
        }

        return new AggregateBaseline(schemaName, DateTimeOffset.UtcNow, snapshots);
    }

    private static async Task<List<string>> listProjectionTablesAsync(NpgsqlConnection conn, string schemaName, CancellationToken token)
    {
        // mt_doc_* tables hold Marten document storage including projection
        // documents. mt_doc_*_b_N suffixes are hash-partition tables for the
        // multi-tenant partitioning policy — we hash the parent table since
        // its rows are the union of all partitions.
        const string sql = @"
            select table_name
            from information_schema.tables
            where table_schema = @schema
              and table_name like 'mt\_doc\_%' escape '\'
              and table_name not similar to 'mt\_doc\_%\_b\_%' escape '\';";

        var names = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("schema", schemaName);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    private static async Task<TableSnapshot> hashTableAsync(NpgsqlConnection conn, string schemaName, string table, CancellationToken token)
    {
        // `data::text` so jsonb's canonical form is hashed (keys sorted by PG's
        // hash order, no whitespace). order by id::text so the hash is
        // independent of physical row order across partitions.
        var sql = $@"select id::text, data::text from {schemaName}.{table} order by id::text;";

        using var sha = SHA256.Create();
        long count = 0;
        var idBuffer = new byte[64];

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            count++;
            var idStr = reader.GetString(0);
            var dataStr = reader.GetString(1);

            sha.TransformBlock(Encoding.UTF8.GetBytes(idStr), 0, idStr.Length, null, 0);
            sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
            sha.TransformBlock(Encoding.UTF8.GetBytes(dataStr), 0, Encoding.UTF8.GetByteCount(dataStr), null, 0);
            sha.TransformBlock(new byte[] { (byte)'\n' }, 0, 1, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var digest = sha.Hash!;
        return new TableSnapshot(table, count, Convert.ToHexString(digest));
    }

    /// <summary>
    /// Diff two baselines. Returns one human-readable line per discrepancy
    /// — empty list means the two baselines are identical.
    /// </summary>
    public static IReadOnlyList<string> Diff(AggregateBaseline expected, AggregateBaseline actual)
    {
        var diffs = new List<string>();

        var expectedByTable = expected.Tables.ToDictionary(x => x.Table, StringComparer.Ordinal);
        var actualByTable = actual.Tables.ToDictionary(x => x.Table, StringComparer.Ordinal);

        foreach (var table in expectedByTable.Keys.Union(actualByTable.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            var hasExpected = expectedByTable.TryGetValue(table, out var exp);
            var hasActual = actualByTable.TryGetValue(table, out var act);

            if (!hasExpected)
            {
                diffs.Add($"  + {table} appeared in actual ({act!.RowCount} rows) but not in baseline");
                continue;
            }
            if (!hasActual)
            {
                diffs.Add($"  - {table} missing from actual (baseline had {exp!.RowCount} rows)");
                continue;
            }
            if (exp!.RowCount != act!.RowCount)
            {
                diffs.Add($"  ~ {table} row count {exp.RowCount} → {act.RowCount}");
            }
            if (!string.Equals(exp.DataHash, act.DataHash, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add($"  ~ {table} data hash differs (expected {exp.DataHash[..16]}…, actual {act.DataHash[..16]}…)");
            }
        }

        return diffs;
    }

    public static async Task WriteAsync(AggregateBaseline baseline, string filePath, CancellationToken token = default)
    {
        var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, token).ConfigureAwait(false);
    }

    public static async Task<AggregateBaseline> ReadAsync(string filePath, CancellationToken token = default)
    {
        var json = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AggregateBaseline>(json)
            ?? throw new InvalidDataException($"Could not deserialize baseline JSON from {filePath}.");
    }
}
