using System.Reflection;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Npgsql;
using Pgvector;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Projection;

/// <summary>
///     A projection that maintains a table of embeddings: events name the text a document should be
///     embedded from, the text is hashed so unchanged content costs no model call, and what is left is
///     embedded in one batched <see cref="Neutral.IEmbeddingProvider" /> call.
/// </summary>
/// <typeparam name="TId">
///     The identity of the embedded document. Not necessarily the stream id — a projection may key on a
///     member of the event instead, which is what <c>map.Map&lt;T&gt;(content, id)</c> is for.
/// </typeparam>
/// <remarks>
///     <para>
///         <b>The body of this is <see cref="Neutral.VectorProjectionMap{TId}" /> and
///         <see cref="Neutral.VectorEmbeddingPlan{TId}" />, not code in this file</b> (jasperfx#841).
///         Folding a page down to one text per document, hashing it, comparing against the stored hash
///         and batching the model call is identical on every store and had been written three times.
///         What Marten supplies here is the two things only Marten can do: read the current hashes out
///         of its table, and write the rows.
///     </para>
///     <para>
///         ⚠️ <b>The writes go through the session's unit of work, not a connection of this
///         projection's own</b> (marten#5421). The old code opened its own <c>NpgsqlConnection</c> and
///         executed the upserts immediately, so an embedding was committed even when the daemon rolled
///         the page back — leaving the index describing events the store does not have. Queueing them
///         with <see cref="IDocumentOperations.QueueSqlCommand(string, object[])" /> puts them in the
///         same transaction as the shard's progression, so they land together or not at all.
///     </para>
///     <para>
///         Register with
///         <c>opts.Projections.Add(new MyVectorProjection(provider), ProjectionLifecycle.Async)</c> and
///         create the table with
///         <c>opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(schemaName))</c>.
///     </para>
/// </remarks>
public abstract class VectorProjection<TId>: IProjection where TId : notnull
{
    private readonly Neutral.IEmbeddingProvider _provider;
    private readonly string _tableName;
    private readonly Neutral.VectorProjectionMap<TId> _map = new();
    private readonly IAggregateLoader? _aggregateLoader;

    protected VectorProjection(string tableName, Neutral.IEmbeddingProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(provider);

        _tableName = tableName;
        _provider = provider;

        Configure(_map);

        if (_map.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Vector projection '{GetType().FullNameInCode()}' declared no mappings at all, so it "
                + "would read every event and write nothing. Map at least one event type in Configure().");
        }

        _aggregateLoader = _map.AggregateType is null ? null : AggregateLoader.For<TId>(_map.AggregateType);
    }

    /// <summary>The table these embeddings live in, unqualified.</summary>
    public string TableName => _tableName;

    /// <summary>
    ///     Declare which events carry the text to embed and which retract a document.
    /// </summary>
    /// <remarks>
    ///     The map is <see cref="Neutral.VectorProjectionMap{TId}" />, which Polecat and Fisher take
    ///     too, so a projection's declaration reads the same whichever store runs it.
    /// </remarks>
    protected abstract void Configure(Neutral.VectorProjectionMap<TId> map);

    /// <summary>
    ///     The Weasel table this projection writes to. Register it with
    ///     <c>opts.Storage.ExtendedSchemaObjects.Add(...)</c>.
    /// </summary>
    /// <remarks>
    ///     The id column is typed from <typeparamref name="TId" /> rather than pinned to <c>uuid</c>
    ///     (marten#5424). A string-identified store could not use this projection at all before, because
    ///     its document ids do not fit in a <c>uuid</c> column and there was no way to say so.
    /// </remarks>
    public virtual Table BuildTable(string schemaName)
    {
        var table = new Table(new PostgresqlObjectName(schemaName, _tableName));
        table.AddColumn<TId>("id").AsPrimaryKey();
        table.AddColumn("embedding", $"vector({_provider.Dimensions})").NotNull();
        table.AddColumn<string>("content_text");
        table.AddColumn<string>("content_hash").NotNull();
        table.AddColumn("metadata", "jsonb");
        table.AddColumn("last_updated", "timestamptz").NotNull().DefaultValueByExpression("now()");
        return table;
    }

    /// <summary>The qualified table name for use in queries.</summary>
    public string QualifiedTableName(string schemaName) => $"{schemaName}.{_tableName}";

    #region IProjection

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        throw new NotSupportedException(
            $"{GetType().FullNameInCode()} calls an embedding model, which is a network round trip, so "
            + "it runs from the async daemon rather than inline on a caller's transaction. Register it "
            + "with ProjectionLifecycle.Async.");
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
        => ApplyAsync(operations, streams.SelectMany(s => s.Events).ToList(), cancellation);

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0) return;

        var plan = Neutral.VectorEmbeddingPlan<TId>.Build(_map, events);

        foreach (var id in plan.AggregateIds)
        {
            var aggregate = await _aggregateLoader!.LoadAsync(operations, id, cancellation).ConfigureAwait(false);
            plan.ApplyAggregate(id, _map, aggregate);
        }

        var store = (DocumentStore)operations.DocumentStore;
        var qualifiedTable = QualifiedTableName(store.Options.Events.DatabaseSchemaName);

        foreach (var id in plan.Deletions)
        {
            operations.QueueSqlCommand($"DELETE FROM {qualifiedTable} WHERE id = ?", id);
        }

        var writes = await plan
            .ResolveAsync(_provider, (ids, token) => ReadHashesAsync(operations, qualifiedTable, ids, token),
                cancellation)
            .ConfigureAwait(false);

        foreach (var write in writes)
        {
            // ⚠️ new Vector(...).ToString(), NEVER embedding.ToString(). The shared provider contract
            // returns ReadOnlyMemory<float>, whose ToString() is "System.ReadOnlyMemory<System.Single>[768]"
            // -- which compiles, binds as text, and is not a vector literal. Pgvector.Vector is what knows
            // how to render "[0.1,0.2,...]", so the conversion happens at this boundary rather than the
            // shared contract carrying a Pgvector type.
            //
            // Bound as text and cast server-side rather than bound as a vector, for the same reason the
            // searches do it: the NpgsqlDataSource caches pg_type on its first connection, so a data
            // source that opened before Marten's migration created the "vector" extension cannot resolve
            // the type and throws.
            operations.QueueSqlCommand(
                $"INSERT INTO {qualifiedTable} (id, embedding, content_text, content_hash, last_updated) "
                + $"VALUES (?, ?::vector({_provider.Dimensions}), ?, ?, now()) "
                + "ON CONFLICT (id) DO UPDATE SET embedding = excluded.embedding, "
                + "content_text = excluded.content_text, content_hash = excluded.content_hash, "
                + "last_updated = now()",
                write.Id,
                new Vector(write.Embedding).ToString(),
                write.Content,
                write.ContentHash);
        }
    }

    #endregion

    /// <summary>
    ///     The hash currently stored for each id, which is the only thing that decides whether the model
    ///     is called at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Lowercased on the way out, and that is worth real money.</b> Marten stored these as
    ///         <b>uppercase</b> hex (<c>Convert.ToHexString</c>) while the shared
    ///         <see cref="Neutral.VectorEmbeddingPlan{TId}.HashOf" /> spells the same SHA-256 bytes in
    ///         <b>lowercase</b> (<c>Convert.ToHexStringLower</c>). Comparing the two as stored would find
    ///         every hash different and re-embed a customer's entire corpus once, at their embedding
    ///         provider's meter, for no change in content. Normalising the read makes the adoption free;
    ///         rows converge to lowercase as their content genuinely changes.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The read opens its own connection while the WRITES ride the session's unit of
    ///         work, and the asymmetry is forced.</b> Marten's daemon session refuses
    ///         <c>IQuerySession.Connection</c> outright — "sticky" connections inside a projection are
    ///         not supported — so a read has nowhere else to go. It is safe: the hashes being compared
    ///         were committed by earlier pages, and a page's own writes are deduplicated by id before
    ///         they get here.
    ///     </para>
    /// </remarks>
    private static async Task<IReadOnlyDictionary<TId, string>> ReadHashesAsync(
        IDocumentOperations operations, string qualifiedTable, IReadOnlyList<TId> ids, CancellationToken token)
    {
        var hashes = new Dictionary<TId, string>();
        if (ids.Count == 0) return hashes;

        await using var conn = operations.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, content_hash FROM {qualifiedTable} WHERE id = ANY($1)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = ids.ToArray() });

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var id = await reader.GetFieldValueAsync<TId>(0, token).ConfigureAwait(false);
            var hash = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
            hashes[id] = hash.ToLowerInvariant();
        }

        return hashes;
    }

    /// <summary>
    ///     Loads the aggregate a <see cref="Neutral.VectorProjectionMap{TId}.MapFromAggregate{T}" />
    ///     mapping builds its text from.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Live aggregation up to the page's last event, deliberately — never a read of an async
    ///     snapshot.</b> The daemon does not order shards against each other, so a vector projection on
    ///     one shard can see a snapshot another shard has not caught up to, and the embedding is then
    ///     built from stale state with nothing to report it. Live aggregation costs one read per affected
    ///     stream per page and cannot be stale.
    /// </remarks>
    private interface IAggregateLoader
    {
        Task<object?> LoadAsync(IDocumentOperations operations, TId id, CancellationToken token);
    }

    private static class AggregateLoader
    {
        public static IAggregateLoader For<T>(Type aggregateType) where T : notnull
        {
            if (typeof(TId) != typeof(Guid) && typeof(TId) != typeof(string))
            {
                throw new NotSupportedException(
                    $"MapFromAggregate builds its text by aggregating the stream live, and Marten "
                    + $"identifies a stream by Guid or by string — not by {typeof(TId).FullNameInCode()}. "
                    + "Use map.Map<TEvent>(content, id) instead, which takes its text from the event.");
            }

            var closed = typeof(LiveAggregateLoader<>).MakeGenericType(typeof(TId), aggregateType);
            return (IAggregateLoader)Activator.CreateInstance(closed)!;
        }
    }

    private sealed class LiveAggregateLoader<TAggregate>: IAggregateLoader where TAggregate : class
    {
        public async Task<object?> LoadAsync(IDocumentOperations operations, TId id, CancellationToken token)
            => id switch
            {
                Guid streamId => await operations.Events
                    .AggregateStreamAsync<TAggregate>(streamId, token: token).ConfigureAwait(false),
                string streamKey => await operations.Events
                    .AggregateStreamAsync<TAggregate>(streamKey, token: token).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Cannot aggregate a stream identified by {id.GetType().Name}.")
            };
    }
}

/// <summary>
///     A <see cref="VectorProjection{TId}" /> keyed on <see cref="Guid" />, with the pre-9.37 fluent
///     mapping API.
/// </summary>
/// <remarks>
///     <para>
///         Kept so an existing projection still compiles and behaves the same. New code should derive
///         from <see cref="VectorProjection{TId}" /> and declare a
///         <see cref="Neutral.VectorProjectionMap{TId}" />, which is the shape Polecat and Fisher share
///         and the only one that can key on something other than a <see cref="Guid" /> (marten#5424) or
///         build its text from aggregate state.
///     </para>
/// </remarks>
public abstract class VectorProjection: VectorProjection<Guid>
{
    /// <summary>
    ///     The pre-9.36 constructor, taking Marten.PgVector's own embedding provider.
    /// </summary>
    /// <remarks>
    ///     Adapted onto the shared contract rather than kept as a second embedding path, so there is one
    ///     place that turns text into vectors and one place that binds them.
    /// </remarks>
    [Obsolete(
        "Supply a JasperFx.Events.Vectors.IEmbeddingProvider instead. This constructor still works and "
        + "adapts.")]
    protected VectorProjection(string tableName, IEmbeddingProvider provider)
        : base(tableName, new LegacyEmbeddingProviderAdapter(provider))
    {
    }

    protected VectorProjection(string tableName, Neutral.IEmbeddingProvider provider)
        : base(tableName, provider)
    {
    }

    /// <summary>Define how events map to the text to embed.</summary>
    protected abstract void Configure(VectorProjectionMapping map);

    protected sealed override void Configure(Neutral.VectorProjectionMap<Guid> map)
    {
        var legacy = new VectorProjectionMapping(map);
        Configure(legacy);
        legacy.AssertDeletesCanAddressTheRowsTheMapsWrote(GetType());
    }
}

/// <summary>
///     The pre-9.37 fluent API for configuring event-to-content mappings, over the shared
///     <see cref="Neutral.VectorProjectionMap{TId}" />.
/// </summary>
/// <remarks>
///     ⚠️ The content selector here sees the event BODY, while the shared map's sees the
///     <see cref="IEvent{T}" /> wrapper. That difference is the reason this façade exists rather than
///     the base class simply exposing the shared map: the wrapper can always reach the body and the
///     body can never reach the wrapper, so the shared shape is the one with more reach — but changing
///     the selector's parameter type under an existing projection would not compile.
/// </remarks>
public class VectorProjectionMapping
{
    private readonly Neutral.VectorProjectionMap<Guid> _map;
    private readonly List<Type> _keyedOnTheEvent = [];
    private readonly List<Type> _deletedByStreamId = [];

    internal VectorProjectionMapping(Neutral.VectorProjectionMap<Guid> map)
    {
        _map = map;
    }

    /// <summary>Map an event type to the text to embed.</summary>
    public VectorProjectionMapping Map<TEvent>(
        Func<TEvent, string> contentSelector,
        Func<TEvent, Guid>? idSelector = null) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(contentSelector);

        if (idSelector is not null) _keyedOnTheEvent.Add(typeof(TEvent));

        // Deliberately NOT wrapped in a catch. A selector that throws is a bug in the projection, and
        // swallowing it returned null -- which reads as "this event contributes no content", exactly the
        // same as a legitimately empty mapping. The document then silently never reaches the index and
        // nothing anywhere reports it (marten#5420). Let it fault the shard instead.
        _map.Map<TEvent>(
            e => contentSelector(e.Data),
            e => idSelector is null ? e.StreamId : idSelector(e.Data));

        return this;
    }

    /// <summary>
    ///     Register an event type that retracts the embedding row. Supply <paramref name="idSelector" />
    ///     whenever the rows are keyed on something other than the stream, exactly as
    ///     <see cref="Map{TEvent}" /> does.
    /// </summary>
    public VectorProjectionMapping Delete<TEvent>(Func<TEvent, Guid>? idSelector = null) where TEvent : notnull
    {
        if (idSelector is null) _deletedByStreamId.Add(typeof(TEvent));

        _map.Delete<TEvent>(e => idSelector is null ? e.StreamId : idSelector(e.Data));

        return this;
    }

    /// <summary>
    ///     A projection whose rows are keyed on a member of the event has to delete by that same member.
    ///     Falling back to the stream id there addresses a row that was never written, so the delete
    ///     matches nothing and the document stays in the index forever — silently, because a DELETE that
    ///     hits no row is not an error. Refuse it while the store is being built instead, which is the
    ///     one moment the caller can still say which member it should have been.
    /// </summary>
    /// <remarks>
    ///     Only the legacy façade needs this check. <see cref="Neutral.VectorProjectionMap{TId}" /> has
    ///     no <c>Delete</c> overload without an id selector at all, so the mistake cannot be spelled.
    /// </remarks>
    internal void AssertDeletesCanAddressTheRowsTheMapsWrote(Type projectionType)
    {
        if (_keyedOnTheEvent.Count == 0 || _deletedByStreamId.Count == 0) return;

        throw new InvalidOperationException(
            $"Vector projection '{projectionType.FullNameInCode()}' maps "
            + $"{string.Join(", ", _keyedOnTheEvent.Select(x => x.Name))} "
            + "to an id taken from the event, but deletes "
            + $"{string.Join(", ", _deletedByStreamId.Select(x => x.Name))} by stream id. The delete would "
            + "address a row that was never written. Supply the same id selector to Delete<T>(), as in "
            + $"map.Delete<{_deletedByStreamId[0].Name}>(e => e.SomeId).");
    }
}
