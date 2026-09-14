using JasperFx.Events.Vectors;
using Marten.Util;
using Weasel.Postgresql.Tables;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     An HNSW index over a document member's embedding, so a vector search is an index scan rather than
///     a sequential scan that computes a distance for every row.
/// </summary>
/// <remarks>
///     <para>
///         <b>Three things have to agree or the index is created and then silently never used</b>, which
///         is the whole reason this is derived from one place rather than spelled by the caller.
///     </para>
///     <list type="number">
///         <item>
///             <b>The indexed expression must be the one the query orders by.</b> Postgres matches an
///             expression index by the expression, so an index over a hand-written variant of
///             <c>(data-&gt;&gt;'x')::vector(n)</c> is valid SQL that serves nothing. Both come from
///             <see cref="PgVectorExtensions" />' own path building.
///         </item>
///         <item>
///             <b>The operator class must match the distance metric.</b> Measured against pgvector 0.8.5:
///             a <c>vector_cosine_ops</c> index serves a <c>&lt;=&gt;</c> query as an
///             <c>Index Scan</c>, and a <c>vector_l2_ops</c> index over the same column serves the same
///             query as a <c>Seq Scan</c> — created without error, reported nowhere, just slow. Hence
///             <see cref="DistanceFunctionExtensions.OpsClass" />.
///         </item>
///         <item>
///             <b>The dimensions must match</b>, being part of the cast and therefore part of the
///             expression.
///         </item>
///     </list>
///     <para>
///         <b><see cref="Columns" /> is resolved lazily, and that is not an optimisation.</b> The JSONB
///         key depends on the serializer's casing, and the serializer is not final when
///         <c>VectorIndex</c> is called — a store configures its serializer and its schema in whatever
///         order the caller writes them. Computing the path at DDL time is what stops a camelCase store
///         from indexing <c>'Embedding'</c> while its searches read <c>'embedding'</c>, which is the
///         silent-empty-result defect one layer down.
///     </para>
/// </remarks>
internal class VectorIndexDefinition: IndexDefinition
{
    private readonly StoreOptions _options;
    private readonly int _dimensions;

    public VectorIndexDefinition(
        StoreOptions options,
        System.Reflection.MemberInfo member,
        int dimensions,
        Neutral.DistanceFunction distance,
        string indexName)
    {
        _options = options;
        Member = member;
        _dimensions = dimensions;
        Distance = distance;

        Name = indexName;

        // "hnsw" is not one of Weasel's known IndexMethod values, which is what CustomMethod is for.
        CustomMethod = "hnsw";

        // Mask is documented as "pattern for surrounding the columns", and an operator class is exactly
        // that: the rendered expression becomes "((…)::vector(n) vector_cosine_ops)".
        Mask = $"? {distance.OpsClass()}";
    }

    /// <summary>The member whose embedding this index covers.</summary>
    public System.Reflection.MemberInfo Member { get; }

    /// <summary>
    ///     The metric this index was built for.
    /// </summary>
    /// <remarks>
    ///     Read back by the searches so a caller who passes no metric gets the one the index declared
    ///     (jasperfx#840) rather than a constant that happens to be right for most corpora. Passing
    ///     <c>Cosine</c> by default over an <c>L2</c> index is not a slow query, it is a DIFFERENT
    ///     ordering — and the index is not used either.
    /// </remarks>
    public Neutral.DistanceFunction Distance { get; }

    /// <summary>
    ///     The indexed expression, built at DDL time from the serializer's casing — see the remarks on
    ///     the class for why this cannot be computed in the constructor.
    /// </summary>
    public override string[]? Columns
    {
        get
        {
            var jsonPath = Member.ToJsonKey(_options.Serializer().Casing);
            return [$"((data ->> '{jsonPath}')::vector({_dimensions}))"];
        }
        set
        {
            // Weasel sets Columns when it reads an existing index back out of the database for delta
            // detection. The declared side is always computed, so the setter is deliberately inert
            // rather than throwing: a difference between the two is what the delta is FOR.
        }
    }
}
