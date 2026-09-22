using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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

        // #5433 / jasperfx#842: refuse a wrong-length query vector by NAME, against the length the
        // index DECLARED.
        //
        // ⚠️ The cast below is built from the QUERY's length, which is why this has to be checked
        // here rather than left to Postgres. A two-element query against a three-dimensional member
        // casts the stored embedding to vector(2) as well, so what comes back is either a Postgres
        // error about dimensions -- a database's words for a caller's mistake -- or, over a table
        // with no matching rows, NO error and an empty list, because the operator is never
        // evaluated. The second is the one worth failing over: "no results" is exactly what a
        // correct search over a sparse corpus looks like.
        AssertQueryVectorLength<T>(store.Options, member, queryVector.Length);

        var dimensions = queryVector.Length;
        var expression = $"(d.data->>'{jsonPath}')::vector({dimensions}) {op} ";

        var where = BuildWhere(
            session,
            new WhereFragment($"d.data->>'{jsonPath}' is not null"),
            filter);

        var storage = ((IMartenSession)session).StorageFor<T>();
        var fields = storage.SelectFields();

        // #5419. An HNSW scan only ever considers hnsw.ef_search candidates, so a caller asking for
        // more than that got fewer rows than `limit` with no error at all. Resolved BEFORE the
        // statement is built, because the settings ride in front of it in the same batch.
        var efSearch = ResolveEfSearch<T>(store.Options, member, limit);

        var builder = await StartBatchAsync(session, efSearch, token).ConfigureAwait(false);

        // #5440. The storage's own columns, so its own selector can read the row -- see ReadAsync.
        builder.Append($"select {string.Join(", ", fields)}, ");
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

        await foreach (var (doc, extra) in ReadAsync(session, storage, builder, token).ConfigureAwait(false))
        {
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

        var storage = ((IMartenSession)session).StorageFor<T>();

        var builder = new BatchBuilder { TenantId = session.TenantId };

        builder.Append($"select {string.Join(", ", storage.SelectFields())}, ts_rank({vector}, ");
        builder.Append($"{queryFunction}('{regConfig}'::regconfig, ");
        builder.AppendParameter(searchText, NpgsqlDbType.Text);
        builder.Append($"))::float8 as rank from {tableName} d WHERE ");
        where.Apply(builder);
        builder.Append(" ORDER BY rank DESC LIMIT ");
        builder.AppendParameter(depth);

        var results = new List<T>();

        await foreach (var (doc, _) in ReadAsync(session, storage, builder, token).ConfigureAwait(false))
        {
            results.Add(doc);
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
    ///     Refuse a query vector whose length is not the one the member's index declared.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Silent when the member declares NO vector index, and deliberately so: Marten's searches
    ///         work without one — the index is what makes them fast, not what makes them possible —
    ///         so there is nothing to check a length against and refusing would break a legitimate
    ///         call. A member with indexes for two metrics declares the same dimensions in each,
    ///         since the length is part of the indexed expression.
    ///     </para>
    /// </remarks>
    private static void AssertQueryVectorLength<T>(StoreOptions options, MemberInfo member, int length)
    {
        var declared = options.Storage.MappingFor(typeof(T)).Indexes
            .OfType<VectorIndexDefinition>()
            .Where(x => x.Member == member)
            .Select(x => x.Dimensions)
            .Distinct()
            .ToArray();

        if (declared.Length == 0 || Array.IndexOf(declared, length) >= 0)
        {
            return;
        }

        throw new ArgumentException(
            $"The query vector has {length} dimensions, and "
            + $"'{typeof(T).FullNameInCode()}.{member.Name}' declares "
            + $"{string.Join(" or ", declared)}. A vector search compares lengths, so this cannot be "
            + "answered: embed the query with the same model the stored embeddings came from.",
            "queryVector");
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

    /// <summary>
    ///     A batch with this search's HNSW scan settings already in front of it, so the statement the
    ///     caller appends next runs under them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>The <c>SET LOCAL</c>s are statements in the SAME batch as the search, and that is
    ///         the whole reason the search can run on the session's connection at all</b> (#5423
    ///         over #5419). They cannot be separate round trips: <c>SET LOCAL</c> outside a transaction
    ///         block is a <c>WARNING</c> and no effect, which would silently put the 40-row truncation
    ///         #5438 fixed straight back. Postgres runs every statement Npgsql sends between two
    ///         <c>Sync</c> messages in an <b>implicit transaction block</b>, so a batch is a
    ///         transaction whether or not the session opened one — the settings take effect for the
    ///         statement behind them and are discarded when the batch ends. Verified against
    ///         PostgreSQL 17 / pgvector 0.8.5, and pinned by
    ///         <c>the_scan_settings_do_not_outlive_the_search</c>.
    ///     </para>
    ///     <para>
    ///         When the session <em>does</em> own a transaction — <c>SessionOptions.ForTransaction</c>,
    ///         or anything past <c>BeginTransaction</c> — the settings are scoped to THAT transaction
    ///         instead, so they outlive the statement and end at the session's commit or rollback.
    ///         That is deliberate and is the safe direction: neither GUC affects anything but an HNSW
    ///         index scan, a later search sets its own <c>ef_search</c>, and a leftover
    ///         <c>iterative_scan</c> can only make another scan return MORE of what it was asked for.
    ///         What matters is that no spelling of this can ride a pooled connection out of the
    ///         session, which a bare <c>SET</c> would.
    ///     </para>
    /// </remarks>
    private static async Task<BatchBuilder> StartBatchAsync(
        IQuerySession session, int? efSearch, CancellationToken token)
    {
        var builder = new BatchBuilder { TenantId = session.TenantId };

        if (!efSearch.HasValue)
        {
            return builder;
        }

        // Interpolated rather than bound, because a GUC assignment takes a literal -- and it is an
        // int that Math.Clamp produced, never anything a caller spelled.
        builder.Append($"SET LOCAL hnsw.ef_search = {efSearch.Value}");
        builder.StartNewCommand();

        if (await SupportsIterativeScanAsync(session, token).ConfigureAwait(false))
        {
            // ⚠️ strict_order rather than relaxed_order, and the difference is not a preference here.
            // relaxed_order may return rows slightly out of distance order, and VectorMatch<T> promises
            // nearest-first -- DocumentSearchCompliance asserts the distances come back ascending. A
            // faster search that breaks its own ordering contract is the wrong trade.
            //
            // This is what makes a FILTERED search return `limit` rows: pgvector applies a predicate
            // AFTER the index scan, so without iterative scan a selective filter still under-returns
            // however large ef_search is.
            builder.Append("SET LOCAL hnsw.iterative_scan = strict_order");
            builder.StartNewCommand();
        }

        return builder;
    }

    /// <summary>
    ///     Run a leg and materialise each row through the document storage's own selector, with the
    ///     leg's score read from the column after the storage's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Executed through the SESSION rather than a connection of its own</b> (#5423). A
    ///         search used to open a second pooled connection, which meant it could not see the
    ///         session's own uncommitted writes — a caller inside <c>SessionOptions.ForTransaction</c>
    ///         could <c>Store</c> a document, <c>SaveChangesAsync</c>, and then fail to find it with a
    ///         search on that same session, while <c>Query&lt;T&gt;()</c> found it. Going through
    ///         <see cref="QuerySession.ExecuteReaderAsync(NpgsqlBatch, CancellationToken)" /> also
    ///         picks up the session's command timeout, resilience pipeline and
    ///         <see cref="IMartenSessionLogger" />, none of which a hand-opened connection had.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The storage's selector, never <c>serializer.FromJson&lt;T&gt;</c></b> (#5440). Both
    ///         legs used to select <c>d.data</c> alone and deserialise it as <c>T</c>, so a search over a
    ///         document hierarchy's base type handed back every <c>Article</c> and <c>Video</c> as a
    ///         plain <c>Content</c>, subclass members lost, where <c>Query&lt;Content&gt;()</c> resolves
    ///         each row's concrete type from <c>mt_doc_type</c>. Selecting
    ///         <see cref="IDocumentStorage.SelectFields" /> and resolving with
    ///         <see cref="ISelectClause.BuildSelector" /> is exactly what LINQ does, so the searches pick
    ///         up the discriminator, metadata columns, and the session's identity map the same way and
    ///         cannot drift from it again.
    ///     </para>
    ///     <para>
    ///         The score is the column immediately after the storage's, the shape
    ///         <c>StatsSelectClause</c> already uses for its count. The selector reads its columns by
    ///         ordinal from zero, so an extra column after them is never seen by it.
    ///     </para>
    /// </remarks>
    private static async IAsyncEnumerable<(T Document, double Extra)> ReadAsync<T>(
        IQuerySession session,
        IDocumentStorage<T> storage,
        BatchBuilder builder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) where T : notnull
    {
        var selector = (ISelector<T>)storage.BuildSelector((IStorageSession)session);
        var scoreOrdinal = storage.SelectFields().Length;

        await using var batch = builder.Compile();

        await using var reader = await session.As<QuerySession>()
            .ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        // The scan settings StartBatchAsync put in front of the search return no rows, and Npgsql
        // lands the reader on the first statement that does. Advancing past any field-less result set
        // rather than trusting that is one line, and it is the difference between this breaking
        // loudly and returning nothing at all if Npgsql ever surfaces them.
        while (reader.FieldCount == 0 && await reader.NextResultAsync(token).ConfigureAwait(false))
        {
        }

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
            var extra = await reader.GetFieldValueAsync<double>(scoreOrdinal, token).ConfigureAwait(false);

            if (document is null) continue;
            yield return (document, extra);
        }
    }

    /// <summary>
    ///     How many candidates the HNSW scan has to consider for this search, or null when there is no
    ///     HNSW index and the scan is exact (#5419).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Clamped to 1000 because that is a HARD LIMIT, not a preference.</b> pgvector
    ///         validates <c>hnsw.ef_search</c> against 1..1000 and <em>errors</em> outside it, so a
    ///         search with <c>limit: 2000</c> would start throwing where it used to return a truncated
    ///         list. Trading a silent truncation for an exception is not the fix this is.
    ///     </para>
    ///     <para>
    ///         Never lowered below pgvector's own default of 40 either: a search for three rows should
    ///         not get worse recall than it does today just because the number is small.
    ///     </para>
    ///     <para>
    ///         Null when the member has no vector index, and that is the common case worth keeping
    ///         cheap — without an index the scan is exact and already returns <c>limit</c> rows, so
    ///         there is nothing to tune and no reason to pay for a transaction.
    ///     </para>
    /// </remarks>
    internal static int? ResolveEfSearch<T>(StoreOptions options, MemberInfo member, int rowsNeeded)
    {
        var indexed = options.Storage.MappingFor(typeof(T)).Indexes
            .OfType<VectorIndexDefinition>()
            .Any(x => x.Member == member);

        return indexed ? Math.Clamp(rowsNeeded, DefaultEfSearch, MaxEfSearch) : null;
    }

    /// <summary>pgvector's own default for <c>hnsw.ef_search</c>.</summary>
    private const int DefaultEfSearch = 40;

    /// <summary>pgvector validates <c>hnsw.ef_search</c> against 1..1000 and errors outside it.</summary>
    private const int MaxEfSearch = 1000;

    /// <summary>
    ///     The pgvector versions known to have <c>hnsw.iterative_scan</c>, keyed by database.
    /// </summary>
    /// <remarks>
    ///     Keyed by the database identifier because the answer is a property of the server rather than
    ///     of a store, and looked up once so the detection costs one round trip per database for the
    ///     life of the process.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, bool> _supportsIterativeScan = new();

    /// <summary>
    ///     Whether this server's pgvector has <c>hnsw.iterative_scan</c>, which arrived in 0.8.0.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Detected rather than attempted, because pgvector reserves the <c>hnsw</c> GUC prefix:
    ///         setting a name it does not know is an ERROR, and an error inside the batch this decides
    ///         the shape of would abort the search rather than degrade it.
    ///     </para>
    ///     <para>
    ///         It has to be its own round trip because its answer is what decides whether the
    ///         <c>SET LOCAL</c> goes into the search's batch at all — but only ever ONE round trip per
    ///         database for the life of the process, which is why the result is cached by database
    ///         identifier rather than by store. That is what the old connection-owned version cost
    ///         too; it simply spent it inside the search's own transaction.
    ///     </para>
    /// </remarks>
    private static async Task<bool> SupportsIterativeScanAsync(IQuerySession session, CancellationToken token)
    {
        var database = session.As<QuerySession>().Database;

        if (_supportsIterativeScan.TryGetValue(database.Identifier, out var known))
        {
            return known;
        }

        await using var cmd = new NpgsqlCommand("select extversion from pg_extension where extname = 'vector'");

        string? raw = null;
        await using (var reader = await session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false) &&
                !await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                raw = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            }
        }

        var supported = Version.TryParse(raw, out var version) && version >= new Version(0, 8);

        _supportsIterativeScan[database.Identifier] = supported;
        return supported;
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

    /// <summary>
    ///     Delegates to the one validator rather than carrying a second copy of the pattern.
    /// </summary>
    /// <remarks>
    ///     GHSA-frqq-p5g3-8jq5 happened because this check lived as a private copy inside
    ///     <c>FullTextWhereFragment</c> and a new code path could not reuse it, so it simply went
    ///     unguarded. This assembly had a third copy — identical regex, so not vulnerable, but one
    ///     edit away from drifting from the original. There is now one pattern, in one place.
    /// </remarks>
    private static void AssertRegConfig(string regConfig) => RegConfigValidation.Validate(regConfig);

    private sealed class SimpleWhereFragmentHolder: IWhereFragmentHolder
    {
        public List<ISqlFragment> Fragments { get; } = [];

        public void Register(ISqlFragment filter)
        {
            if (filter is not null) Fragments.Add(filter);
        }
    }
}
