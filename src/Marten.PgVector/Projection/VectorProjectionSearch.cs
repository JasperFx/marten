using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Npgsql;
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

        var sql = $"SELECT id, embedding {op} $1 as distance, content_text " +
                  $"FROM {qualifiedTable} " +
                  $"ORDER BY embedding {op} $1 LIMIT $2";

        var results = new List<VectorSearchResult>();

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = queryVector });
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
