using System.Linq.Expressions;
using System.Reflection;
using Pgvector;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     The pre-9.36 Marten.PgVector API, kept so that code written against it still compiles and still
///     works.
/// </summary>
/// <remarks>
///     <para>
///         <b>These types were deleted when the package moved onto <c>JasperFx.Events.Vectors</c>'
///         contracts, and deleting them was the wrong call.</b> The neutral contracts are what let the
///         same application code name a metric once and hand it to Marten, Polecat or Fisher — that
///         part stands — but nothing about adopting them required taking the old spelling away. Both
///         work now; the old one is marked obsolete so it says so at the call site rather than in a
///         release note.
///     </para>
///     <para>
///         Everything here is one file on purpose, so a future major version removes it by deleting a
///         file rather than by unpicking it.
///     </para>
///     <para>
///         ⚠️ <b>One consequence cannot be avoided while both spellings exist.</b> A file that imports
///         BOTH <c>Marten.PgVector</c> and <c>JasperFx.Events.Vectors</c> and then writes a bare
///         <c>DistanceFunction</c> or <c>IEmbeddingProvider</c> gets CS0104, because two types of that
///         name are in scope. Qualify the one you mean. Existing code, which imports only
///         <c>Marten.PgVector</c>, is unaffected — that is the case this file exists to protect.
///     </para>
///     <para>
///         ⚠️ <b>Inside this package the shared types are always QUALIFIED, and that is load-bearing.</b>
///         A member of a namespace beats a using-alias declared outside it, so restoring these types into
///         <c>Marten.PgVector</c> silently recaptured every unqualified <c>DistanceFunction</c> in the
///         package's own files — and because both enums declare the same three members, nearly all of it
///         still compiled while the PUBLIC SIGNATURES had reverted to the legacy type. Aliasing does not
///         fix it; qualifying does.
///     </para>
/// </remarks>
file static class LegacyApiDoc;

/// <summary>
///     The distance metric, in Marten.PgVector's own spelling.
/// </summary>
/// <remarks>
///     ⚠️ <b>Converted by NAME, never by value.</b> This enum declares <c>L2</c> first and the neutral
///     one declares <c>Cosine</c> first, so a cast would turn an L2 search into a cosine search
///     silently — same underlying number, different meaning. <see cref="LegacyDistanceExtensions" />
///     switches on the member.
/// </remarks>
[Obsolete(
    "Use JasperFx.Events.Vectors.DistanceFunction, which the other Critter Stack stores share. "
    + "This spelling still works and is converted by name.")]
public enum DistanceFunction
{
    L2,

    Cosine,

    InnerProduct
}

/// <summary>Maps the legacy metric onto the shared one.</summary>
public static class LegacyDistanceExtensions
{
    /// <summary>The shared <c>JasperFx.Events.Vectors.DistanceFunction</c> this names.</summary>
    [Obsolete("Only needed while the legacy DistanceFunction is in use.")]
    public static Neutral.DistanceFunction ToNeutral(this DistanceFunction distance) => distance switch
    {
        DistanceFunction.L2 => Neutral.DistanceFunction.L2,
        DistanceFunction.Cosine => Neutral.DistanceFunction.Cosine,
        DistanceFunction.InnerProduct => Neutral.DistanceFunction.InnerProduct,
        _ => throw new ArgumentOutOfRangeException(nameof(distance))
    };
}

/// <summary>
///     A vector field declaration that never had any effect.
/// </summary>
/// <remarks>
///     ⚠️ <b>Nothing ever read these registrations.</b> <c>UsePgVector()</c> takes no options object and
///     no code path consumed one, so <see cref="PgVectorOptions.VectorOn{TDoc}" /> has always been a
///     no-op. It is restored because removing a public type is a breaking change whether or not it did
///     anything — but a caller who wants what it LOOKS like it does wants
///     <c>StoreOptions.VectorIndex(...)</c>, which declares a real HNSW index.
/// </remarks>
internal class VectorFieldRegistration
{
    public Type DocumentType { get; }
    public MemberInfo Member { get; }
    public int Dimensions { get; }
    public int Distance { get; }
    public string ColumnName { get; }

    public VectorFieldRegistration(Type documentType, MemberInfo member, int dimensions,
        int distance, string? columnName)
    {
        DocumentType = documentType;
        Member = member;
        Dimensions = dimensions;
        Distance = distance;
        ColumnName = columnName ?? member.Name.ToLowerInvariant();
    }
}

/// <inheritdoc cref="VectorFieldRegistration" />
[Obsolete(
    "This never had any effect — nothing consumed the registrations. Use StoreOptions.VectorIndex(...), "
    + "which declares a real HNSW index.")]
public class PgVectorOptions
{
    internal List<VectorFieldRegistration> Registrations { get; } = new();

    public PgVectorOptions VectorOn<TDoc>(
        Expression<Func<TDoc, object?>> memberExpression,
        int dimensions,
        DistanceFunction distance = DistanceFunction.Cosine,
        string? columnName = null)
    {
        var member = GetMemberInfo(memberExpression);
        Registrations.Add(new VectorFieldRegistration(
            typeof(TDoc), member, dimensions, (int)distance, columnName));
        return this;
    }

    private static MemberInfo GetMemberInfo<TDoc>(Expression<Func<TDoc, object?>> expression)
    {
        var body = expression.Body;

        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        return body switch
        {
            MemberExpression memberExpr => memberExpr.Member,
            _ => throw new ArgumentException("Expression must be a simple property or field access")
        };
    }
}

/// <summary>
///     The pre-9.36 search overloads, taking the legacy <see cref="DistanceFunction" />.
/// </summary>
/// <remarks>
///     ⚠️ <b>The distance parameter deliberately has NO default here, and that is what keeps the
///     overloads unambiguous.</b> The shared-contract overloads default every argument, so a call that
///     omits the metric has exactly one candidate. Giving this one a default too would make
///     <c>VectorSearchAsync(x =&gt; x.Embedding, vector)</c> ambiguous — a compile error introduced by
///     the very change meant to avoid breaking callers.
/// </remarks>
public static class LegacyVectorSearchExtensions
{
    /// <inheritdoc cref="PgVectorExtensions.VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, Vector, int, Neutral.DistanceFunction)" />
    [Obsolete(
        "Pass a JasperFx.Events.Vectors.DistanceFunction instead. This overload still works and "
        + "converts by name.")]
    public static Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        Vector queryVector,
        int limit,
        DistanceFunction distance) where T : class
        // Called as a static rather than through extension syntax: the legacy overloads in this
        // class are in scope here too, and extension-method resolution prefers them, so the
        // forward would bind to itself.
        => PgVectorExtensions.VectorSearchAsync(
            session, vectorProperty, queryVector.Memory, limit, distance.ToNeutral());

    /// <inheritdoc cref="VectorSearchAsync{T}(IQuerySession, Expression{Func{T, object}}, Vector, int, DistanceFunction)" />
    [Obsolete(
        "Pass a JasperFx.Events.Vectors.DistanceFunction instead. This overload still works and "
        + "converts by name.")]
    public static Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        Vector queryVector,
        DistanceFunction distance) where T : class
        => PgVectorExtensions.VectorSearchAsync(
            session, vectorProperty, queryVector.Memory, 10, distance.ToNeutral());
}
