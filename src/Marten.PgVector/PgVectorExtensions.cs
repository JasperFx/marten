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

        return opts;
    }

    /// <summary>
    /// Search for documents by vector similarity using a dedicated vector column.
    /// The vector data is stored as a float array in the JSONB document and queried
    /// via a cast to the vector type.
    /// </summary>
    /// <summary>
    ///     Declare an HNSW index over a document member's embedding, so
    ///     <see cref="VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, ReadOnlyMemory{float}, int, DistanceFunction)" />
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
        DistanceFunction distance = DistanceFunction.Cosine,
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
        DistanceFunction distance = DistanceFunction.Cosine) where T : class
    {
        var matches = await session
            .VectorSearchWithScoresAsync(vectorProperty, queryVector, limit, distance)
            .ConfigureAwait(false);

        return matches.Select(x => x.Document).ToList();
    }

    /// <inheritdoc cref="VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, ReadOnlyMemory{float}, int, DistanceFunction)" />
    /// <remarks>The Pgvector-typed spelling, kept so existing call sites still compile.</remarks>
    public static Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        Vector queryVector,
        int limit = 10,
        DistanceFunction distance = DistanceFunction.Cosine) where T : class
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
    public static async Task<IReadOnlyList<VectorMatch<T>>> VectorSearchWithScoresAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        DistanceFunction distance = DistanceFunction.Cosine) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();

        var member = GetMemberInfo(vectorProperty);

        // Build a JSONB path to the vector property. ToJsonKey, not member.Name: the key has to be
        // spelled the way the serializer wrote it, or the path matches nothing and the search returns
        // an empty list with no error.
        var jsonPath = member.ToJsonKey(store.Options.Serializer().Casing);

        var op = distance.Operator();
        var dimensions = queryVector.Length;

        var whereClause = $"d.data->>'{jsonPath}' IS NOT NULL";
        var tenantId = session.TenantId;
        var isSingleDatabase = store.Options.Tenancy.Cardinality == JasperFx.Descriptors.DatabaseCardinality.Single;
        var hasTenantFilter = isSingleDatabase
            && !string.IsNullOrEmpty(tenantId)
            && tenantId != JasperFx.StorageConstants.DefaultTenantId;

        if (hasTenantFilter)
        {
            whereClause += " AND d.tenant_id = $3";
        }

        // The distance is SELECTED as well as ordered by, so the score comes back with the row rather
        // than being recomputed. Npgsql can bind a Vector directly, but the data source caches pg_type
        // the first time it opens a connection -- if the "vector" extension is created later (e.g. by
        // Marten's schema migration on the same data source), the cache is stale and parameter
        // resolution throws "Cannot resolve 'vector' to a fully qualified datatype name." Routing
        // through text + an explicit cast makes this race-immune.
        var vectorSql = $"(d.data->>'{jsonPath}')::vector({dimensions}) {op} $1::vector({dimensions})";
        var sql = $"select d.data, {vectorSql} as distance from {tableName} d " +
                  $"WHERE {whereClause} " +
                  $"ORDER BY {vectorSql} LIMIT $2";

        var results = new List<VectorMatch<T>>();

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter
        {
            Value = new Vector(queryVector).ToString(), NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text
        });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });
        if (hasTenantFilter)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        }

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var doc = serializer.FromJson<T>(new MemoryStream(bytes));
            if (doc == null) continue;

            var distanceValue = await reader.GetFieldValueAsync<double>(1).ConfigureAwait(false);
            results.Add(new VectorMatch<T>(doc, distanceValue));
        }

        return results;
    }

    private static MemberInfo GetMemberInfo<T>(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        return body switch
        {
            MemberExpression memberExpr => memberExpr.Member,
            _ => throw new ArgumentException("Expression must be a simple property or field access")
        };
    }
}
