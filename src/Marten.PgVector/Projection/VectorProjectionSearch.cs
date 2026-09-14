using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Projection;

/// <summary>
///     One row of a <see cref="VectorProjection{TId}" />'s embedding table, with the distance it
///     matched at.
/// </summary>
/// <remarks>
///     <para>
///         Generic over the id because the projection is (marten#5424). The non-generic
///         <see cref="VectorSearchResult" /> below is the <see cref="Guid" /> shape, kept for the call
///         sites that already exist.
///     </para>
///     <para>
///         <c>Distance</c> is a <see cref="double" /> here where <see cref="VectorSearchResult" /> had a
///         <see cref="float" />, matching <see cref="Neutral.VectorMatch{T}" /> — one declared width
///         beats a per-store difference a consumer would have to know.
///     </para>
/// </remarks>
public sealed record VectorProjectionMatch<TId>(TId Id, double Distance, string? ContentText) where TId : notnull;

/// <summary>
///     Result of a vector similarity search against a <see cref="VectorProjection" />'s table.
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
    ///     Search a vector projection's embedding table by similarity.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>This table is not a Marten document table</b>, so there is no tenancy or soft-delete
    ///     predicate to apply and none is applied. A projection written per tenant lives in that
    ///     tenant's database, which is why this reads the SESSION's database rather than the store's
    ///     default one.
    /// </remarks>
    public static async Task<IReadOnlyList<VectorProjectionMatch<TId>>> VectorProjectionSearchAsync<TId>(
        this IQuerySession session,
        string projectionTableName,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionTableName);

        var store = (DocumentStore)session.DocumentStore;
        var schemaName = store.Options.Events.DatabaseSchemaName;
        var qualifiedTable = $"{schemaName}.{projectionTableName}";
        var op = distance.Operator();
        var dimensions = queryVector.Length;

        // See VectorSearchRunner — bind the query vector as its text form and cast to vector(N)
        // server-side, because the NpgsqlDataSource type-info cache is stale when the "vector" extension
        // was created at migration time on the same data source.
        var sql = $"SELECT id, (embedding {op} $1::vector({dimensions}))::float8 as distance, content_text " +
                  $"FROM {qualifiedTable} " +
                  $"ORDER BY embedding {op} $1::vector({dimensions}) LIMIT $2";

        var results = new List<VectorProjectionMatch<TId>>();

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter
        {
            Value = new Vector(queryVector).ToString(), NpgsqlDbType = NpgsqlDbType.Text
        });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            results.Add(new VectorProjectionMatch<TId>(
                await reader.GetFieldValueAsync<TId>(0).ConfigureAwait(false),
                await reader.GetFieldValueAsync<double>(1).ConfigureAwait(false),
                await reader.IsDBNullAsync(2).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<string>(2).ConfigureAwait(false)));
        }

        return results;
    }

    /// <inheritdoc cref="VectorProjectionSearchAsync{TId}" />
    /// <remarks>The pre-9.37 spelling, kept so existing call sites still compile.</remarks>
    public static async Task<IReadOnlyList<VectorSearchResult>> VectorProjectionSearchAsync(
        this IQuerySession session,
        string projectionTableName,
        Vector queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine)
    {
        var matches = await session
            .VectorProjectionSearchAsync<Guid>(projectionTableName, queryVector.Memory, limit, distance)
            .ConfigureAwait(false);

        return matches
            .Select(x => new VectorSearchResult
            {
                Id = x.Id, Distance = (float)x.Distance, ContentText = x.ContentText
            })
            .ToList();
    }
}
