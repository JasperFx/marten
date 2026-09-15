using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

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
///         ⚠️ <b>The options record, the result record and the text style are
///         <c>JasperFx.Events.Vectors</c>' now, not Marten's own</b> (jasperfx#840). Marten's copy
///         defaulted <c>Distance</c> to <c>Cosine</c> while Polecat and Fisher had no such default, so
///         the same code over an L2 index gave a cosine ordering on Marten and an L2 ordering on the
///         other two, with nothing reported. The shared record defaults it to null, meaning "the metric
///         the index declared" — see <see cref="VectorSearchRunner.ResolveDistance{T}" />.
///     </para>
///     <para>
///         ⚠️ <b>The fusion reads ORDINAL POSITION, not the legs' scores, and that is the load-bearing
///         choice.</b> <c>ts_rank</c> and cosine distance are not on a comparable scale and do not even
///         run in the same direction, so normalising them into one number means picking constants that
///         are wrong for somebody's corpus. RRF needs only each leg's ordering, so the two need no
///         calibration against each other and the behaviour does not move when the embedding model or
///         the text configuration changes. The fusion itself is
///         <see cref="Neutral.ReciprocalRankFusion" /> — one implementation for all three stores, which
///         is also the only one that can fuse legs of DIFFERENT document types (jasperfx#844).
///     </para>
///     <para>
///         <b>Two statements fused in memory, not one.</b> One statement would be a join whose plan
///         neither index serves — and both legs run through
///         <see cref="VectorSearchRunner" />, so they inherit its tenancy, soft-delete and filter
///         handling rather than restating them.
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
        Neutral.HybridSearchOptions? options = null,
        CancellationToken token = default) where T : class
    {
        var matches = await session
            .HybridSearchWithScoresAsync(vectorProperty, searchText, queryVector, limit, options, token)
            .ConfigureAwait(false);

        return matches.Select(x => x.Document).ToList();
    }

    /// <inheritdoc cref="HybridSearchAsync{T}" />
    /// <summary>The best <paramref name="limit" /> documents, each with its fused score.</summary>
    public static Task<IReadOnlyList<Neutral.HybridMatch<T>>> HybridSearchWithScoresAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        string searchText,
        ReadOnlyMemory<float> queryVector,
        int limit = 10,
        Neutral.HybridSearchOptions? options = null,
        CancellationToken token = default) where T : class
        => HybridSearchRunner.RunAsync(session, vectorProperty, searchText, queryVector, limit, options, null,
            token);
}

/// <summary>
///     The body of a hybrid search, shared by the extension method and by the store-neutral
///     <see cref="Neutral.IDocumentSearchOperations" /> implementation.
/// </summary>
internal static class HybridSearchRunner
{
    public static async Task<IReadOnlyList<Neutral.HybridMatch<T>>> RunAsync<T>(
        IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        string searchText,
        ReadOnlyMemory<float> queryVector,
        int limit,
        Neutral.HybridSearchOptions? options,
        Expression<Func<T, bool>>? filter,
        CancellationToken token) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(vectorProperty);
        ArgumentNullException.ThrowIfNull(searchText);

        options ??= new Neutral.HybridSearchOptions();

        // Every option check the three stores were each making for themselves, in one place. A store
        // cannot quietly stop applying one, and the messages cannot drift.
        var depth = options.ResolveCandidateDepth(limit);

        // ⚠️ REFUSED rather than ignored (#5446, jasperfx#854). Marten weights full-text columns at
        // INDEX time through WeightedFullTextIndex, so there is nothing a per-call weight could be
        // applied to here. Ignoring it is the one option that is actually dangerous: a caller who
        // weighted their title column and silently got an unweighted ranking has no way to find out,
        // because the search still returns plausible documents in a plausible order — the same failure
        // shape as the Distance default the shared record was created to fix.
        options.AssertColumnWeightsAreNotSupported(
            "Marten",
            "Weight full-text columns at index time with WeightedFullTextIndex instead. Fisher is the "
            + "store that honours per-call column weights.");

        var textLeg = await VectorSearchRunner
            .TextLegAsync(session, searchText, options, depth, filter, token)
            .ConfigureAwait(false);

        var vectorLeg = await VectorSearchRunner
            .VectorLegAsync(session, vectorProperty, queryVector, depth, options.Distance, filter, token)
            .ConfigureAwait(false);

        var store = (DocumentStore)session.DocumentStore;
        var idMember = store.Options.Storage.MappingFor(typeof(T)).IdMember;

        return Neutral.ReciprocalRankFusion.Fuse<T, object>(
            textLeg,
            vectorLeg.Select(x => x.Document).ToList(),
            document => ReadIdentity(idMember, document)
                        ?? throw new InvalidOperationException(
                            $"Document of type {typeof(T).FullNameInCode()} came back with no identity, "
                            + "so the two legs cannot be fused."),
            limit,
            options.K);
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
}
