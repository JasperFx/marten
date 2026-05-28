using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Marten.PgVector.Projection;

/// <summary>
/// Result of a vector similarity search against a VectorProjection table.
/// </summary>
public class VectorSearchResult
{
    public Guid Id { get; set; }
    public float Distance { get; set; }
    public string? ContentText { get; set; }
}

public static class VectorProjectionSearchExtensions
{
    /// <summary>
    /// Search a VectorProjection's embedding table by similarity.
    /// </summary>
    public static async Task<IReadOnlyList<VectorSearchResult>> VectorProjectionSearchAsync(
        this IQuerySession session,
        string projectionTableName,
        Vector queryVector,
        int limit = 10,
        DistanceFunction distance = DistanceFunction.Cosine)
    {
        var store = (DocumentStore)session.DocumentStore;
        var schemaName = store.Options.Events.DatabaseSchemaName;
        var qualifiedTable = $"{schemaName}.{projectionTableName}";
        var op = distance.Operator();
        var dimensions = queryVector.ToArray().Length;

        // See PgVectorExtensions.VectorSearchAsync — bind the query vector as its
        // text form and cast to vector(N) server-side to bypass the
        // NpgsqlDataSource type-info cache (which can be stale when the "vector"
        // extension is created at migration time on the same data source).
        var sql = $"SELECT id, embedding {op} $1::vector({dimensions}) as distance, content_text " +
                  $"FROM {qualifiedTable} " +
                  $"ORDER BY embedding {op} $1::vector({dimensions}) LIMIT $2";

        var results = new List<VectorSearchResult>();

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = queryVector.ToString(), NpgsqlDbType = NpgsqlDbType.Text });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new VectorSearchResult
            {
                Id = reader.GetGuid(0),
                Distance = reader.GetFloat(1),
                ContentText = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return results;
    }
}
