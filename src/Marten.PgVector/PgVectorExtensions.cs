using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Pgvector.Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Vectors;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

public static class PgVectorExtensions
{
    /// <summary>
    /// Enable pgvector support for this Marten store. This registers the PostgreSQL
    /// "vector" extension and configures Npgsql to handle vector types on all
    /// data sources, including tenant databases.
    /// </summary>
    public static StoreOptions UsePgVector(this StoreOptions opts)
    {
        // Configure all NpgsqlDataSourceBuilders to support pgvector types
        opts.ConfigureNpgsqlDataSourceBuilder(b => b.UseVector());

        // Register the PostgreSQL "vector" extension for schema management.
        // This ensures CREATE EXTENSION IF NOT EXISTS vector runs on every database.
        opts.Storage.ExtendedSchemaObjects.Add(new Extension("vector"));

        // The store-agnostic route to search (jasperfx#842). Core Marten declares
        // IDocumentReadOperations.Search and holds this seam; the pgvector SQL that satisfies it lives
        // here, in the optional package, so core takes no dependency on it. Without this line a session
        // opened by this store answers Search with a NotSupportedException, which is the correct answer
        // for a store that has no vector search.
        opts.SearchOperations = session => new PgVectorSearchOperations(session);

        return opts;
    }

    /// <summary>
    ///     Declare an HNSW index over a document member's embedding, so
    ///     <see cref="VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, ReadOnlyMemory{float}, int, Neutral.DistanceFunction)" />
    ///     is served by an index rather than a sequential scan.
    /// </summary>
    /// <param name="dimensions">
    ///     The embedding's length. Part of the cast and therefore part of the indexed expression, so it
    ///     has to be the length the searches actually bind.
    /// </param>
    /// <param name="distance">
    ///     ⚠️ <b>Must be the metric the searches use.</b> An index built for one metric is not used by a
    ///     query in another — created without error, and a sequential scan forever. Declare one index per
    ///     metric a member is actually searched by.
    /// </param>
    /// <param name="m">pgvector's <c>m</c> — max connections per layer. pgvector's default is 16.</param>
    /// <param name="efConstruction">
    ///     pgvector's <c>ef_construction</c> — candidate list size while building. pgvector's default
    ///     is 64. Higher builds slower and recalls better.
    /// </param>
    /// <remarks>
    ///     The index goes on the document's own mapping rather than into
    ///     <c>StorageFeatures.ExtendedSchemaObjects</c>, so it is created with the table it indexes, is
    ///     seen by Marten's delta detection, and is dropped with the table by the cleaner. A loose schema
    ///     object would have to be ordered against a table it knows nothing about.
    /// </remarks>
    public static StoreOptions VectorIndex<T>(
        this StoreOptions opts,
        Expression<Func<T, object?>> vectorProperty,
        int dimensions,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine,
        int? m = null,
        int? efConstruction = null)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions),
                "A vector index needs the embedding's length; it is part of the indexed expression.");
        }

        var member = GetMemberInfo(vectorProperty);
        var mapping = opts.Storage.MappingFor(typeof(T));

        // Named per (member, metric) rather than per member, because declaring the same member for two
        // metrics is legitimate -- each serves queries the other cannot -- and two indexes cannot share
        // a name.
        var indexName =
            $"idx_{mapping.TableName.Name}_{member.Name.ToLowerInvariant()}_{distance.ToString().ToLowerInvariant()}";

        var index = new VectorIndexDefinition(opts, member, dimensions, distance, indexName);

        if (m.HasValue) index.StorageParameters["m"] = m.Value;
        if (efConstruction.HasValue) index.StorageParameters["ef_construction"] = efConstruction.Value;

        mapping.Indexes.Add(index);

        return opts;
    }

    /// <summary>
    ///     The nearest <paramref name="limit" /> documents to <paramref name="queryVector" />, closest
    ///     first.
    /// </summary>
    /// <remarks>
    ///     Takes <see cref="ReadOnlyMemory{T}" /> rather than a Pgvector type, so the same call reads the
    ///     same against Marten, Polecat and Fisher — it is what
    ///     <see cref="JasperFx.Events.Vectors.IEmbeddingProvider" /> hands back.
    /// </remarks>
    public static async Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine) where T : class
    {
        var matches = await session
            .VectorSearchWithScoresAsync(vectorProperty, queryVector, limit, distance)
            .ConfigureAwait(false);

        return matches.Select(x => x.Document).ToList();
    }

    /// <inheritdoc cref="VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, ReadOnlyMemory{float}, int, Neutral.DistanceFunction)" />
    /// <remarks>The Pgvector-typed spelling, kept so existing call sites still compile.</remarks>
    public static Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        Vector queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine) where T : class
        => session.VectorSearchAsync(vectorProperty, queryVector.Memory, limit, distance);

    /// <summary>
    ///     The nearest <paramref name="limit" /> documents, each with the distance it matched at.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Every metric is a DISTANCE — smaller is closer — including inner product.</b>
    ///         pgvector's <c>&lt;#&gt;</c> returns the negative inner product for exactly that reason, so
    ///         the number handed back is directly comparable across the three metrics' orderings and one
    ///         ascending sort serves all of them.
    ///     </para>
    ///     <para>
    ///         The score is what a caller needs to fuse this leg with another one — a reciprocal-rank
    ///         fusion reads the ordinal position, but a threshold or a confidence cut needs the value,
    ///         and recomputing it client-side would mean re-reading every embedding.
    ///     </para>
    /// </remarks>
    public static Task<IReadOnlyList<VectorMatch<T>>> VectorSearchWithScoresAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        Neutral.DistanceFunction distance = Neutral.DistanceFunction.Cosine) where T : class
        => VectorSearchRunner.VectorLegAsync<T>(
            session, vectorProperty, queryVector, limit, distance, filter: null, CancellationToken.None);

    private static MemberInfo GetMemberInfo<T>(Expression<Func<T, object?>> expression)
        => VectorSearchRunner.GetMemberInfo(expression);
}
