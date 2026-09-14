using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema.Indexing.FullText;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector;

/// <summary>
///     The one place that turns a vector or text leg into SQL and runs it.
/// </summary>
/// <remarks>
///     <para>
///         Every public entry point — the extension methods on <see cref="IQuerySession" /> and the
///         store-neutral <see cref="Neutral.IDocumentSearchOperations" /> — lands here, so the two
///         cannot drift in what they filter or how they bind. The extensions pass
///         <c>filter: null</c>; only the neutral contract carries a caller predicate (jasperfx#843).
///     </para>
///     <para>
///         ⚠️ <b>Built through Weasel's <see cref="BatchBuilder" /> rather than a hand-numbered
///         <c>NpgsqlCommand</c>, and that is what makes a caller's predicate possible at all.</b> A
///         predicate parsed out of a LINQ expression brings parameters of its own, and Npgsql refuses a
///         command whose parameters mix positional (<c>$1</c>) and named (<c>:p0</c>) spellings.
///         <see cref="BatchBuilder" /> is the positional one and is what Marten's own query pipeline
///         uses, so everything on the statement — ours and the fragment's — is numbered by one thing.
///     </para>
/// </remarks>
internal static class VectorSearchRunner
{
    /// <summary>
    ///     The nearest documents, each with the distance it matched at.
    /// </summary>
    public static async Task<IReadOnlyList<Neutral.VectorMatch<T>>> VectorLegAsync<T>(
        IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        ReadOnlyMemory<float> queryVector,
        int limit,
        Neutral.DistanceFunction? distance,
        Expression<Func<T, bool>>? filter,
        CancellationToken token) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(vectorProperty);

        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be at least 1");
        }

        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();

        var member = GetMemberInfo(vectorProperty);

        // ToJsonKey, not member.Name: the key has to be spelled the way the serializer wrote it, or the
        // path matches nothing and the search returns an empty list with no error.
        var jsonPath = member.ToJsonKey(store.Options.Serializer().Casing);

        var op = ResolveDistance<T>(store.Options, member, distance).Operator();
        var dimensions = queryVector.Length;
        var expression = $"(d.data->>'{jsonPath}')::vector({dimensions}) {op} ";

        var where = BuildWhere(
            session,
            new WhereFragment($"d.data->>'{jsonPath}' is not null"),
            filter);

        var builder = new BatchBuilder { TenantId = session.TenantId };

        builder.Append("select d.data, ");
        builder.Append(expression);

        // Bound as text and cast server-side rather than bound as a vector: the NpgsqlDataSource caches
        // pg_type on its first connection, so a data source that opened before Marten's migration
        // created the "vector" extension cannot resolve 'vector' and parameter binding throws.
        builder.AppendParameter(new Vector(queryVector).ToString(), NpgsqlDbType.Text);

        // ⚠️ $1 by hand in the ORDER BY, deliberately, and it is safe only because the query vector is
        // the FIRST parameter appended to this statement. The distance has to be both selected and
        // ordered by -- selected so the score comes back with the row rather than being recomputed,
        // ordered by the same expression so pgvector matches it to the HNSW index. Appending the
        // parameter twice would bind the vector twice and, worse, give the ORDER BY a different
        // placeholder than the indexed expression uses.
        builder.Append($"::vector({dimensions}) as distance from {tableName} d WHERE ");
        where.Apply(builder);
        builder.Append($" ORDER BY {expression}$1::vector({dimensions}) LIMIT ");
        builder.AppendParameter(limit);

        var results = new List<Neutral.VectorMatch<T>>();
        var serializer = store.Serializer;

        await foreach (var (json, extra) in ReadAsync(session, builder, token).ConfigureAwait(false))
        {
            var doc = serializer.FromJson<T>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
            if (doc is null) continue;

            results.Add(new Neutral.VectorMatch<T>(doc, extra));
        }

        return results;
    }

    /// <summary>
    ///     The text leg of a hybrid search — the full-text predicate, ordered by <c>ts_rank</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Raw SQL, like the vector leg beside it. What this leg has to guarantee either way is an
    ///         explicit <c>ts_rank</c> order: a text leg taken in an undefined order would make its RANK
    ///         meaningless, and rank is the one thing reciprocal rank fusion reads.
    ///     </para>
    ///     <para>
    ///         ⚠️ The tsvector expression comes from <see cref="FullTextIndexResolver.ResolveVector" />,
    ///         which is what the <c>WHERE</c> side of every Marten full-text query already uses.
    ///         Spelling it again here would be a second copy free to drift from the indexed expression —
    ///         and a mismatch there is not an error, it is an index that is silently never used.
    ///     </para>
    /// </remarks>
    public static async Task<IReadOnlyList<T>> TextLegAsync<T>(
        IQuerySession session,
        string searchText,
        Neutral.HybridSearchOptions options,
        int depth,
        Expression<Func<T, bool>>? filter,
        CancellationToken token) where T : notnull
    {
        var store = (DocumentStore)session.DocumentStore;
        var mapping = store.Options.Storage.MappingFor(typeof(T));
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();

        var regConfig = options.RegConfig ?? DefaultRegConfig;

        // The regconfig is interpolated rather than bound, as it is everywhere else in Marten: binding
        // it ruins the query plan. It is validated rather than trusted.
        AssertRegConfig(regConfig);

        var vector = FullTextIndexResolver.ResolveVector(mapping, regConfig);

        var queryFunction = options.TextStyle == Neutral.HybridTextStyle.WebStyle
            ? "websearch_to_tsquery"
            : "plainto_tsquery";

        var tsquery = $"{queryFunction}('{regConfig}'::regconfig, ?)";

        var where = BuildWhere(session, new WhereFragment($"{vector} @@ {tsquery}", searchText), filter);

        var builder = new BatchBuilder { TenantId = session.TenantId };

        builder.Append($"select d.data, ts_rank({vector}, ");
        builder.Append($"{queryFunction}('{regConfig}'::regconfig, ");
        builder.AppendParameter(searchText, NpgsqlDbType.Text);
        builder.Append($"))::float8 as rank from {tableName} d WHERE ");
        where.Apply(builder);
        builder.Append(" ORDER BY rank DESC LIMIT ");
        builder.AppendParameter(depth);

        var results = new List<T>();
        var serializer = store.Serializer;

        await foreach (var (json, _) in ReadAsync(session, builder, token).ConfigureAwait(false))
        {
            var doc = serializer.FromJson<T>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
            if (doc is not null) results.Add(doc);
        }

        return results;
    }

    /// <summary>The Postgres text-search configuration used when a caller names none.</summary>
    public const string DefaultRegConfig = "english";

    /// <summary>
    ///     The metric a search with no explicit one runs under: the metric the member's index declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>This is the behavioral change jasperfx#840 exists for.</b> Marten's own
    ///         <c>HybridSearchOptions.Distance</c> defaulted to <c>Cosine</c>, so a member indexed for
    ///         <c>L2</c> and searched with no metric named got a COSINE ordering — a different result
    ///         set from the same code on Polecat or Fisher, and one the HNSW index could not serve
    ///         either, since pgvector matches an index to a query by its operator. The shared option
    ///         defaults to null, which means this.
    ///     </para>
    ///     <para>
    ///         With no index declared there is nothing to read, so <c>Cosine</c> stands as the fallback —
    ///         it is what text embedding models are trained for. With two indexes over the same member
    ///         for different metrics — which is legitimate, each serving queries the other cannot — there
    ///         is no single answer, so the caller is asked rather than guessed at.
    ///     </para>
    /// </remarks>
    public static Neutral.DistanceFunction ResolveDistance<T>(
        StoreOptions options, MemberInfo member, Neutral.DistanceFunction? declared)
    {
        if (declared.HasValue) return declared.Value;

        var indexes = options.Storage.MappingFor(typeof(T)).Indexes
            .OfType<VectorIndexDefinition>()
            .Where(x => x.Member == member)
            .Select(x => x.Distance)
            .Distinct()
            .ToArray();

        return indexes.Length switch
        {
            0 => Neutral.DistanceFunction.Cosine,
            1 => indexes[0],
            _ => throw new InvalidOperationException(
                $"'{typeof(T).FullNameInCode()}.{member.Name}' has vector indexes for "
                + $"{string.Join(" and ", indexes)}, so there is no single metric the index 'declared'. "
                + "Name the one this search should use.")
        };
    }

    /// <summary>
    ///     The complete <c>WHERE</c>: this search's own predicate, the caller's, and the ones Marten
    ///     would have applied to <c>Query&lt;T&gt;()</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b><see cref="IDocumentStorage.FilterDocuments" /> is what closes two gaps at once.</b>
    ///         Neither leg had a soft-delete predicate, so a vector search returned documents the store
    ///         considers deleted — Polecat and Fisher both filter them. And the tenancy condition was
    ///         hand-written against <c>Tenancy.Cardinality</c>, which is not the same question as
    ///         <c>TenancyStyle.Conjoined</c>: a conjoined type read under the default tenant got no
    ///         filter at all. Routing through the storage's own wrapper means the searches filter
    ///         exactly what a LINQ query would, including a document hierarchy's discriminator, and
    ///         cannot fall behind it.
    ///     </para>
    ///     <para>
    ///         A caller predicate is applied BEFORE the limit, not after, which is why it is spliced
    ///         into this statement rather than run over the results: the answer is the top-k of the
    ///         filtered set, not the filtered remains of the top-k.
    ///     </para>
    /// </remarks>
    private static ISqlFragment BuildWhere<T>(
        IQuerySession session, ISqlFragment baseFilter, Expression<Func<T, bool>>? filter) where T : notnull
    {
        var storage = ((IMartenSession)session).StorageFor<T>();

        var combined = baseFilter;

        if (filter is not null)
        {
            var holder = new SimpleWhereFragmentHolder();
            var parser = new WhereClauseParser(
                ((IMartenSession)session).Options,
                ((ILinqDocumentStorage)storage).QueryMembers,
                holder);

            parser.Visit(filter.Body);

            var parsed = holder.Fragments.Count switch
            {
                0 => throw new BadLinqExpressionException(
                    "The filter expression did not produce a WHERE clause at all."),
                1 => holder.Fragments[0],
                _ => CompoundWhereFragment.And(holder.Fragments)
            };

            // A sub-query filter -- x => x.Children.Any(c => ...) -- is not self-contained; it needs a
            // CTE placed above the statement, which a hand-built one has nowhere to put. Marten's own
            // patch and delete-where paths refuse it for the same reason, and refusing is far better
            // than emitting SQL that silently means something else.
            if (parsed.ContainsAny<ISubQueryFilter>())
            {
                throw new BadLinqExpressionException(
                    "A filter using a sub-query over a child collection is not supported by vector or "
                    + "hybrid search. Filter on a member of the document itself, or duplicate the value "
                    + "you need to filter on.");
            }

            combined = CompoundWhereFragment.And([baseFilter, parsed]);
        }

        return (ISqlFragment)storage.FilterDocuments(combined, (IStorageSession)session);
    }

    private static async IAsyncEnumerable<(string Json, double Extra)> ReadAsync(
        IQuerySession session,
        BatchBuilder builder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var batch = builder.Compile();
        batch.Connection = conn;

        await using var reader = await batch.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var extra = await reader.GetFieldValueAsync<double>(1, token).ConfigureAwait(false);
            yield return (json, extra);
        }
    }

    internal static MemberInfo GetMemberInfo<T>(Expression<Func<T, object?>> expression)
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

    private static void AssertRegConfig(string regConfig)
    {
        if (!Regex.IsMatch(regConfig, @"^[a-zA-Z_][a-zA-Z0-9_]{0,62}(\.[a-zA-Z_][a-zA-Z0-9_]{0,62})?$"))
        {
            throw new ArgumentException(
                $"Invalid PostgreSQL text-search configuration name '{regConfig}'. It is interpolated "
                + "into SQL rather than bound, because binding it ruins the query plan, so it has to be "
                + "a simple identifier.", nameof(regConfig));
        }
    }

    private sealed class SimpleWhereFragmentHolder: IWhereFragmentHolder
    {
        public List<ISqlFragment> Fragments { get; } = [];

        public void Register(ISqlFragment filter)
        {
            if (filter is not null) Fragments.Add(filter);
        }
    }
}
