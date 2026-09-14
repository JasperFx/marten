using System.Linq.Expressions;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     Marten's implementation of the store-neutral similarity-search contract, reached through
///     <c>IDocumentReadOperations.Search</c> (jasperfx#842).
/// </summary>
/// <remarks>
///     <para>
///         <b>This is what makes vector search reachable from store-agnostic code at all.</b> Every
///         store's entry point is an extension method on that store's own <c>IQuerySession</c> which
///         casts to store internals in its first statement, so a library written against
///         <c>IDocumentReadOperations</c> could not ask for "the ten nearest documents by embedding"
///         without referencing all three store packages.
///     </para>
///     <para>
///         ⚠️ <b>Reached through an accessor rather than by putting these members on the session, and
///         that is not stylistic.</b> Marten.PgVector already ships extension methods named
///         <c>VectorSearchWithScoresAsync</c> and <c>HybridSearchWithScoresAsync</c> on
///         <see cref="IQuerySession" />. An instance member of the same name would win overload
///         resolution over the extension at every existing call site — silently, with no error and
///         different behavior. Behind an accessor that collision cannot happen.
///     </para>
///     <para>
///         The only behavioral difference from the extension methods is what the contract adds: a
///         <c>filter</c> predicate, and a nullable <c>distance</c> whose null means "the metric the
///         index declared" rather than an unconditional cosine.
///     </para>
/// </remarks>
internal sealed class PgVectorSearchOperations(IQuerySession session): Neutral.IDocumentSearchOperations
{
    public Task<IReadOnlyList<Neutral.VectorMatch<T>>> VectorSearchWithScoresAsync<T>(
        Expression<Func<T, object?>> member,
        ReadOnlyMemory<float> query,
        int limit = 10,
        Neutral.DistanceFunction? distance = null,
        Expression<Func<T, bool>>? filter = null,
        CancellationToken token = default) where T : notnull
        => VectorSearchRunner.VectorLegAsync(session, member, query, limit, distance, filter, token);

    public Task<IReadOnlyList<Neutral.HybridMatch<T>>> HybridSearchWithScoresAsync<T>(
        Expression<Func<T, object?>> vectorMember,
        string text,
        ReadOnlyMemory<float> query,
        int limit = 10,
        Neutral.HybridSearchOptions? options = null,
        Expression<Func<T, bool>>? filter = null,
        CancellationToken token = default) where T : notnull
        => HybridSearchRunner.RunAsync(session, vectorMember, text, query, limit, options, filter, token);
}
