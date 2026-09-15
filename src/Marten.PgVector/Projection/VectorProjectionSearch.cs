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
    ///     <para>
    ///         ⚠️ <b>This table is not a Marten document table</b>, so none of Marten's implicit
    ///         predicates apply automatically — soft deletes and document hierarchies have no meaning
    ///         here. <b>Tenancy does</b>, and used not to be applied (marten#5420): under conjoined
    ///         tenancy every tenant's embeddings share one table, and this returned all of them,
    ///         <c>content_text</c> included. It now filters on the session's tenant, the way
    ///         <c>VectorSearchWithScoresAsync</c> already did for documents.
    ///     </para>
    ///     <para>
    ///         Database-per-tenant was never affected and still works the same way: a projection
    ///         written per tenant lives in that tenant's database, which is why this runs through the
    ///         SESSION rather than the store's default database.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Run through the session rather than a connection of its own</b> (#5423), so it
    ///         participates in whatever transaction the session is in and picks up its command timeout,
    ///         resilience pipeline and logger. The embedding table carries no HNSW index, so unlike
    ///         <see cref="VectorSearchRunner" /> there is nothing here to size <c>hnsw.ef_search</c>
    ///         for — an exact scan already returns the full limit.
    ///     </para>
    /// </remarks>
    public static async Task<IReadOnlyList<VectorProjectionMatch<TId>>> VectorProjectionSearchAsync<TId>(
        this IQuerySession session,
        string projectionTableName,
        ReadOnlyMemory<float> queryVector,
        int limit,
        Neutral.DistanceFunction distance,
        CancellationToken token) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionTableName);

        var store = (DocumentStore)session.DocumentStore;
        var schemaName = store.Options.Events.DatabaseSchemaName;
        var qualifiedTable = $"{schemaName}.{projectionTableName}";
        var op = distance.Operator();
        var dimensions = queryVector.Length;

        // marten#5420. Null under single tenancy, where the table has no tenant_id column at all.
        var tenantId = store.Options.Events.TenancyStyle == TenancyStyle.Conjoined
            ? session.TenantId
            : null;

        // See VectorSearchRunner — bind the query vector as its text form and cast to vector(N)
        // server-side, because the NpgsqlDataSource type-info cache is stale when the "vector" extension
        // was created at migration time on the same data source.
        // ⚠️ The predicate goes in the WHERE, not into a post-filter over the top-k. Filtering after
        // the ORDER BY would return fewer than `limit` rows for the asking tenant whenever another
        // tenant's embeddings happen to be nearer -- the same jasperfx#843 rule the document search
        // follows.
        var sql = $"SELECT id, (embedding {op} $1::vector({dimensions}))::float8 as distance, content_text " +
                  $"FROM {qualifiedTable} " +
                  (tenantId is null ? "" : "WHERE tenant_id = $3 ") +
                  $"ORDER BY embedding {op} $1::vector({dimensions}) LIMIT $2";

        var results = new List<VectorProjectionMatch<TId>>();

        await using var cmd = new NpgsqlCommand(sql);
        cmd.Parameters.Add(new NpgsqlParameter
        {
            Value = new Vector(queryVector).ToString(), NpgsqlDbType = NpgsqlDbType.Text
        });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });
        if (tenantId is not null)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        }

        await using var reader = await session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            results.Add(new VectorProjectionMatch<TId>(
                await reader.GetFieldValueAsync<TId>(0, token).ConfigureAwait(false),
                await reader.GetFieldValueAsync<double>(1, token).ConfigureAwait(false),
                await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false)));
        }

        return results;
    }

    /// <inheritdoc cref="VectorProjectionSearchAsync{TId}(IQuerySession, string, ReadOnlyMemory{float}, int, Neutral.DistanceFunction, CancellationToken)" />
    /// <remarks>
    ///     ⚠️ <b>A separate overload rather than a defaulted <c>token</c> on the one above</b>, because
    ///     adding an optional parameter to a shipped method changes its signature in metadata and every
    ///     assembly compiled against the old one throws <c>MissingMethodException</c> without being
    ///     recompiled. This shape is the one that already shipped, so it keeps every default it had and
    ///     the token-taking overload takes none — which is also what keeps the two unambiguous.
    /// </remarks>
    public static Task<IReadOnlyList<VectorProjectionMatch<TId>>> VectorProjectionSearchAsync<TId>(
        this IQuerySession session,
        string projectionTableName,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine) where TId : notnull
        => session.VectorProjectionSearchAsync<TId>(
            projectionTableName, queryVector, limit, distance, CancellationToken.None);

    /// <inheritdoc cref="VectorProjectionSearchAsync{TId}(IQuerySession, string, ReadOnlyMemory{float}, int, Neutral.DistanceFunction, CancellationToken)" />
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

    /// <inheritdoc cref="VectorProjectionSearchAsync{TId}(IQuerySession, string, ReadOnlyMemory{float}, int, Neutral.DistanceFunction, CancellationToken)" />
    /// <remarks>The pre-9.37 spelling, with a cancellation token.</remarks>
    public static async Task<IReadOnlyList<VectorSearchResult>> VectorProjectionSearchAsync(
        this IQuerySession session,
        string projectionTableName,
        Vector queryVector,
        int limit,
        Neutral.DistanceFunction distance,
        CancellationToken token)
    {
        var matches = await session
            .VectorProjectionSearchAsync<Guid>(projectionTableName, queryVector.Memory, limit, distance, token)
            .ConfigureAwait(false);

        return matches
            .Select(x => new VectorSearchResult
            {
                Id = x.Id, Distance = (float)x.Distance, ContentText = x.ContentText
            })
            .ToList();
    }
}
