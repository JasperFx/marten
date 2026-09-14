using JasperFx.Events.Vectors;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     How a <see cref="Neutral.DistanceFunction" /> is spelled in pgvector — the operator a query orders by,
///     and the operator class an index has to be built with.
/// </summary>
/// <remarks>
///     <para>
///         <b>The enum itself is <see cref="JasperFx.Events.Vectors.DistanceFunction" /> now, not a
///         Marten-local copy.</b> It is the same three members with the same meaning, and sharing it is
///         what lets application code name a metric once and hand it to whichever store it is running
///         against — Marten, Polecat or Fisher. Marten.PgVector had its own, which compiled fine and
///         made a store-agnostic caller pick a side.
///     </para>
///     <para>
///         ⚠️ <b>Every metric is a DISTANCE, so smaller is closer, including inner product.</b> pgvector's
///         <c>&lt;#&gt;</c> returns the NEGATIVE inner product for exactly that reason, so one
///         <c>ORDER BY … ASC</c> serves all three and the promise <see cref="Neutral.DistanceFunction" /> makes
///         on every store holds here without a special case.
///     </para>
/// </remarks>
internal static class DistanceFunctionExtensions
{
    /// <summary>The pgvector operator that measures this distance.</summary>
    public static string Operator(this Neutral.DistanceFunction f) => f switch
    {
        Neutral.DistanceFunction.L2 => "<->",
        Neutral.DistanceFunction.Cosine => "<=>",
        Neutral.DistanceFunction.InnerProduct => "<#>",
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };

    /// <summary>
    ///     The operator class an HNSW or IVFFlat index must declare to serve this distance.
    /// </summary>
    /// <remarks>
    ///     An index built with the wrong class is created without error and then never used, because
    ///     pgvector matches an index to a query by its operator. That silence is why the index DDL and
    ///     the query operator are both derived from the same enum here rather than spelled separately.
    /// </remarks>
    public static string OpsClass(this Neutral.DistanceFunction f) => f switch
    {
        Neutral.DistanceFunction.L2 => "vector_l2_ops",
        Neutral.DistanceFunction.Cosine => "vector_cosine_ops",
        Neutral.DistanceFunction.InnerProduct => "vector_ip_ops",
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };
}
