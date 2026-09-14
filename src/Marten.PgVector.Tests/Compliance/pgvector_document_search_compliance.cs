using System;
using System.Linq.Expressions;
using JasperFx.Events.ComplianceTests;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.PgVector.Tests.Compliance;

/// <summary>
///     The document compliance fixture, plus the two things a search suite needs that core Marten
///     cannot supply: <c>UsePgVector()</c> and the declared indexes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Here rather than in Marten.Testing, and the reason is structural rather than tidiness.</b>
///         <c>IDocumentReadOperations.Search</c> is reached from core Marten's session, but the
///         implementation behind it is pgvector SQL in an optional package — core holds
///         <c>StoreOptions.SearchOperations</c> as a hole and <c>UsePgVector()</c> fills it. A fixture in
///         Marten.Testing could not call that without Marten.Testing referencing Marten.PgVector, which
///         is the dependency arrow backwards. So the base fixture grew a <c>Configure</c> hook and this
///         subclass is the whole of what is Marten-PgVector-specific.
///     </para>
///     <para>
///         ⚠️ <b>Marten is the store jasperfx#842 warns about.</b> Its vector search is served by an
///         HNSW index, and pgvector applies a predicate AFTER an index scan bounded by
///         <c>hnsw.ef_search</c> (default 40) — so the filter facts, which assert that a predicate
///         excluding every globally-nearest row still returns the full limit, are the ones that can
///         legitimately fail here where an exact-scan store gets them for free. That is marten#5419, a
///         real difference rather than a bug in the suite. The suite's corpus is three documents,
///         deliberately small enough to sit inside any sane bound, so a failure here would be a
///         finding rather than the known limit.
///     </para>
/// </remarks>
public class MartenPgVectorDocumentComplianceFixture: MartenDocumentComplianceFixture
{
    /// <summary>
    ///     jasperfx#842: Marten implements <c>IDocumentSearchOperations</c> through Marten.PgVector.
    /// </summary>
    public override bool SupportsVectorSearch => true;

    /// <summary>
    ///     Hybrid search — the <c>tsvector</c> leg and the vector leg fused by reciprocal rank fusion.
    /// </summary>
    public override bool SupportsHybridSearch => true;

    protected override void Configure(StoreOptions options, DocumentComplianceConfig config)
    {
        options.UsePgVector();

        // ⚠️ The declarations are not optional and they do not degrade gracefully. A wrong-length
        // query vector is refused by NAME against the DECLARED dimensions, and the metric is part of
        // the index rather than of the query — an HNSW index built for one metric is created without
        // error and then silently never used by a query in another, which is a sequential scan
        // forever rather than a failure. So the fixture carries the metric across rather than taking
        // the DSL's default.
        foreach (var declaration in config.VectorIndexes)
        {
            // VectorIndex is an extension method on StoreOptions rather than an instance member, so
            // the reflected target is the static class and the options are the first argument.
            typeof(PgVectorExtensions)
                .GetMethod(nameof(PgVectorExtensions.VectorIndex))!
                .MakeGenericMethod(declaration.DocumentType)
                .Invoke(null, [
                    options,
                    ExpressionFor(declaration.DocumentType, declaration.MemberName, typeof(object)),
                    declaration.Dimensions,
                    declaration.Distance,
                    null,
                    null
                ]);
        }

        foreach (var declaration in config.FullTextIndexes)
        {
            // Schema.For<T>().FullTextIndex(params Expression<Func<T, object>>[]) — note `object`
            // rather than `object?` here, unlike the vector DSL, so the array's element type has to
            // match or the params argument will not bind.
            var expressions = Array.CreateInstance(
                typeof(Expression<>).MakeGenericType(
                    typeof(Func<,>).MakeGenericType(declaration.DocumentType, typeof(object))),
                declaration.MemberNames.Length);

            for (var i = 0; i < declaration.MemberNames.Length; i++)
            {
                expressions.SetValue(
                    ExpressionFor(declaration.DocumentType, declaration.MemberNames[i], typeof(object)), i);
            }

            var mappingExpression = typeof(MartenRegistry)
                .GetMethod(nameof(MartenRegistry.For))!
                .MakeGenericMethod(declaration.DocumentType)
                .Invoke(options.Schema, null)!;

            mappingExpression.GetType()
                .GetMethod(nameof(MartenRegistry.DocumentMappingExpression<object>.FullTextIndex),
                    [expressions.GetType()])!
                .Invoke(mappingExpression, [expressions]);
        }
    }

    /// <summary>
    ///     <c>x =&gt; (object)x.Member</c>, built from a member name.
    /// </summary>
    /// <remarks>
    ///     The shared declarations carry a <see cref="Type" /> and a member NAME because a record has
    ///     no type parameter to write a lambda against; both DSLs here take one, so the name is turned
    ///     back into an expression. The conversion to <c>object</c> is what makes a value-type member
    ///     assignable to the DSL's parameter, and Marten's member resolution unwraps it.
    /// </remarks>
    private static LambdaExpression ExpressionFor(Type documentType, string memberName, Type returnType)
    {
        var parameter = Expression.Parameter(documentType, "x");
        var body = Expression.Convert(Expression.PropertyOrField(parameter, memberName), returnType);

        return Expression.Lambda(
            typeof(Func<,>).MakeGenericType(documentType, returnType), body, parameter);
    }
}

/*
 * jasperfx#842 / #843 — the shared SEARCH suite, and the first time those facts have run against
 * Marten's engine rather than against a design argument.
 *
 * In the PgVector collection because every class in it builds a store that runs
 * `CREATE EXTENSION IF NOT EXISTS vector`, which PostgreSQL does not make race-safe: two callers can
 * both pass the existence check before either inserts into pg_extension, and the loser gets 23505 on
 * pg_extension_name_index. See PgVectorCollection.
 *
 * What the suite holds is nearest-first, that the score is a DISTANCE on the vector side and the
 * OPPOSITE on the hybrid side, that Marten's implicit predicates (tenancy, soft delete, hierarchy)
 * apply as they do to Query<T>(), and that a filter narrows BEFORE the limit. It is deliberately NOT
 * a relevance suite — the tokenizer, the regConfig and the HNSW parameters are Marten's own, and
 * Marten.PgVector.Tests owns them.
 */

[Collection("Marten.PgVector")]
public class pgvector_document_search_compliance
    : DocumentSearchCompliance<MartenPgVectorDocumentComplianceFixture>;
