using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using JasperFx.Events.Vectors;
using Marten.Internal.Sessions;
using Marten.Schema.Indexing.FullText;
using Npgsql;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     Knobs for <see cref="HybridSearchExtensions.HybridSearchAsync{T}" />.
/// </summary>
/// <param name="K">
///     Reciprocal rank fusion's smoothing constant. 60 is the value the literature uses and there is
///     rarely a reason to move it; lowering it sharpens the advantage of a first-place finish.
/// </param>
/// <param name="CandidateDepth">
///     How deep each leg reads before fusing. Defaults to <c>max(limit × 4, 50)</c>, and must be at
///     least <c>limit</c>. A document ranked 40th by one leg and 1st by the other is the result hybrid
///     search exists for, and reading only <c>limit</c> from each leg would never see it.
/// </param>
/// <param name="Distance">The metric the vector leg uses.</param>
/// <param name="TextStyle">How the text is turned into a tsquery. See <see cref="HybridTextStyle" />.</param>
/// <param name="RegConfig">The Postgres text-search configuration, matching the full-text index's.</param>
public sealed record HybridSearchOptions(
    int K = 60,
    int? CandidateDepth = null,
    Neutral.DistanceFunction Distance = Neutral.DistanceFunction.Cosine,
    HybridTextStyle TextStyle = HybridTextStyle.PlainText,
    string RegConfig = "english");

/// <summary>
///     Which tsquery function the text leg uses.
/// </summary>
/// <remarks>
///     <b>Both members are safe to hand a search box's raw contents, and that is why the list is
///     short.</b> <c>to_tsquery</c>'s raw syntax is deliberately absent: it can be malformed, and a
///     malformed query in one leg of a fused search fails the whole call — where in a plain
///     <c>Where(x =&gt; x.Search(...))</c> it fails only the thing the caller asked for. Phrase and
///     ngram search are reachable through <c>Query&lt;T&gt;()</c> and are not what a hybrid search is
///     usually fed.
/// </remarks>
public enum HybridTextStyle
{
    /// <summary>Every word, in any order, with no query syntax at all. The default.</summary>
    PlainText,

    /// <summary>Quoted phrases, <c>or</c> between alternatives, a leading <c>-</c> to exclude.</summary>
    WebStyle
}

/// <summary>
///     A document and the fused score that ranked it.
/// </summary>
/// <remarks>
///     <b>Larger is better</b>, which is the opposite of <see cref="VectorMatch{T}" />'s distance and
///     the same direction as <c>ts_rank</c> — an RRF score is a sum of reciprocals, so it rises with
///     agreement between the legs. The absolute value means little on its own; what it supports is a
///     floor, or a comparison between results of the same query.
/// </remarks>
public sealed record HybridMatch<T>(T Document, double Score);

/// <summary>
///     Hybrid search: reciprocal rank fusion over the <c>ts_rank</c> leg and the vector leg.
/// </summary>
/// <remarks>
///     <para>
///         <b>Keyword and embedding search fail in different directions</b>, which is the entire
///         argument for fusing them. Full text misses a paraphrase that shares no tokens; vector search
///         misses an exact identifier, a product code, or a rare proper noun the model never saw.
///     </para>
///     <para>
///         ⚠️ <b>The fusion reads ORDINAL POSITION, not the legs' scores, and that is the load-bearing
///         choice.</b> <c>ts_rank</c> and cosine distance are not on a comparable scale and do not even
///         run in the same direction, so normalising them into one number means picking constants that
///         are wrong for somebody's corpus. RRF needs only each leg's ordering, so the two need no
///         calibration against each other and the behaviour does not move when the embedding model or
///         the text configuration changes.
///     </para>
///     <para>
///         <b>Two statements fused in memory, not one.</b> One statement would be a join whose plan
///         neither index serves — and the vector leg already runs through
///         <see cref="PgVectorExtensions.VectorSearchWithScoresAsync{T}" />, so it inherits that path's
///         tenant filtering and its parameter binding rather than restating them.
///     </para>
///     <para>
///         <b>The fusion is over the UNION</b>, so a document only one leg found still scores. That is
///         the point rather than a tolerance: what the keyword leg alone finds is exactly what the
///         vector leg is bad at.
///     </para>
/// </remarks>
public static class HybridSearchExtensions
{
    /// <summary>The best <paramref name="limit" /> documents by fused rank, best first.</summary>
    public static async Task<IReadOnlyList<T>> HybridSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        string searchText,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        HybridSearchOptions? options = null,
        CancellationToken token = default) where T : class
    {
        var matches = await session
            .HybridSearchWithScoresAsync(vectorProperty, searchText, queryVector, limit, options, token)
            .ConfigureAwait(false);

        return matches.Select(x => x.Document).ToList();
    }

    /// <inheritdoc cref="HybridSearchAsync{T}" />
    /// <summary>The best <paramref name="limit" /> documents, each with its fused score.</summary>
    public static async Task<IReadOnlyList<HybridMatch<T>>> HybridSearchWithScoresAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        string searchText,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        HybridSearchOptions? options = null,
        CancellationToken token = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(vectorProperty);
        ArgumentNullException.ThrowIfNull(searchText);

        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be at least 1");
        }

        options ??= new HybridSearchOptions();

        if (options.K < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.K,
                "K must be at least 1. It is reciprocal rank fusion's smoothing constant, and 0 would "
                + "make the top-ranked document of either leg score infinitely.");
        }

        var depth = options.CandidateDepth ?? Math.Max(limit * 4, 50);

        if (depth < limit)
        {
            throw new ArgumentOutOfRangeException(nameof(options), depth,
                $"CandidateDepth ({depth}) is below limit ({limit}), so the fusion would have fewer "
                + "candidates than it is asked to return. It exists to read DEEPER than limit: a "
                + "document ranked low by one leg and first by the other is what hybrid search is for.");
        }

        var textLeg = await TextLegAsync<T>(session, searchText, options, depth, token).ConfigureAwait(false);

        var vectorLeg = await session
            .VectorSearchWithScoresAsync(vectorProperty, queryVector, depth, options.Distance)
            .ConfigureAwait(false);

        var store = (DocumentStore)session.DocumentStore;
        var idMember = store.Options.Storage.MappingFor(typeof(T)).IdMember;

        return Fuse(textLeg, vectorLeg.Select(x => x.Document).ToList(), idMember, options.K, limit);
    }

    /// <summary>
    ///     The text leg — the full-text predicate, ordered by <c>ts_rank</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Raw SQL rather than <c>Query&lt;T&gt;()</c>, because Marten has no relevance ordering for
    ///         full-text search — <c>OrderByNgramRank</c> is ngram search's, and a text leg taken in an
    ///         undefined order would make its RANK meaningless, which is the one thing RRF reads.
    ///     </para>
    ///     <para>
    ///         ⚠️ The tsvector expression comes from <see cref="FullTextIndexResolver.ResolveVector" />,
    ///         which is what the <c>WHERE</c> side of every Marten full-text query already uses. Spelling
    ///         it again here would be a second copy free to drift from the indexed expression — and a
    ///         mismatch there is not an error, it is an index that is silently never used.
    ///     </para>
    /// </remarks>
    private static async Task<IReadOnlyList<T>> TextLegAsync<T>(
        IQuerySession session, string searchText, HybridSearchOptions options, int depth, CancellationToken token)
        where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var mapping = store.Options.Storage.MappingFor(typeof(T));
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();

        var vector = FullTextIndexResolver.ResolveVector(mapping, options.RegConfig);

        var queryFunction = options.TextStyle == HybridTextStyle.WebStyle
            ? "websearch_to_tsquery"
            : "plainto_tsquery";

        // The regconfig is interpolated rather than bound, as it is everywhere else in Marten: binding
        // it ruins the query plan. It is validated below rather than trusted.
        AssertRegConfig(options.RegConfig);

        var tsquery = $"{queryFunction}('{options.RegConfig}'::regconfig, $1)";

        var whereClause = $"{vector} @@ {tsquery}";
        var tenantId = session.TenantId;
        var isSingleDatabase =
            store.Options.Tenancy.Cardinality == JasperFx.Descriptors.DatabaseCardinality.Single;
        var hasTenantFilter = isSingleDatabase
            && !string.IsNullOrEmpty(tenantId)
            && tenantId != JasperFx.StorageConstants.DefaultTenantId;

        if (hasTenantFilter)
        {
            whereClause += " AND d.tenant_id = $3";
        }

        var sql = $"select d.data, ts_rank({vector}, {tsquery}) as rank from {tableName} d " +
                  $"WHERE {whereClause} ORDER BY rank DESC LIMIT $2";

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = searchText });
        cmd.Parameters.Add(new NpgsqlParameter { Value = depth });
        if (hasTenantFilter)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        }

        var results = new List<T>();
        var serializer = store.Serializer;

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var doc = serializer.FromJson<T>(new MemoryStream(bytes));
            if (doc != null) results.Add(doc);
        }

        return results;
    }

    /// <summary>
    ///     Reciprocal rank fusion: <c>score(d) = Σ 1 / (k + rank(d))</c> over the legs that found it.
    /// </summary>
    /// <remarks>
    ///     <para>Ranks are 1-based, which is what makes <c>k</c> mean what the literature says it means.</para>
    ///     <para>
    ///         <b>Ties are broken deterministically, and that is not tidiness.</b> Two documents found at
    ///         the same rank by one leg and by neither in the other have identical scores, which is
    ///         common rather than exotic. Without a total order the page a caller gets differs between
    ///         runs. Best rank first, then the identity, which is unique by construction.
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<HybridMatch<T>> Fuse<T>(
        IReadOnlyList<T> textLeg, IReadOnlyList<T> vectorLeg, MemberInfo idMember, int k, int limit)
        where T : class
    {
        var fused = new Dictionary<object, Candidate<T>>();

        void Accumulate(IReadOnlyList<T> leg)
        {
            for (var i = 0; i < leg.Count; i++)
            {
                var document = leg[i];
                var id = ReadIdentity(idMember, document)
                         ?? throw new InvalidOperationException(
                             $"Document of type {typeof(T).FullNameInCode()} came back with no identity, "
                             + "so the two legs cannot be fused.");
                var rank = i + 1;

                if (fused.TryGetValue(id, out var existing))
                {
                    // The instance kept is the first leg's, deliberately: both legs materialise through
                    // the same serializer over the same row, so they are equal documents, and keeping
                    // one makes reference identity within a result stable.
                    existing.Score += 1.0 / (k + rank);
                    existing.BestRank = Math.Min(existing.BestRank, rank);
                }
                else
                {
                    fused[id] = new Candidate<T>(document, 1.0 / (k + rank), rank, id);
                }
            }
        }

        Accumulate(textLeg);
        Accumulate(vectorLeg);

        return fused.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.BestRank)
            .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .Select(x => new HybridMatch<T>(x.Document, x.Score))
            .ToList();
    }

    /// <summary>
    ///     A document's identity, whether the mapping's id member is a property or a field — Marten
    ///     permits both, and the fusion key has to work for either.
    /// </summary>
    private static object? ReadIdentity(MemberInfo idMember, object document) => idMember switch
    {
        PropertyInfo property => property.GetValue(document),
        FieldInfo field => field.GetValue(document),
        _ => throw new InvalidOperationException(
            $"Cannot read the identity from a {idMember.MemberType} member '{idMember.Name}'.")
    };

    private static void AssertRegConfig(string regConfig)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                regConfig, @"^[a-zA-Z_][a-zA-Z0-9_]{0,62}(\.[a-zA-Z_][a-zA-Z0-9_]{0,62})?$"))
        {
            throw new ArgumentException(
                $"Invalid PostgreSQL text-search configuration name '{regConfig}'. It is interpolated "
                + "into SQL rather than bound, because binding it ruins the query plan, so it has to be "
                + "a simple identifier.", nameof(regConfig));
        }
    }

    private sealed class Candidate<T>(T document, double score, int bestRank, object id)
    {
        public T Document { get; } = document;
        public double Score { get; set; } = score;
        public int BestRank { get; set; } = bestRank;
        public object Id { get; } = id;
    }
}
