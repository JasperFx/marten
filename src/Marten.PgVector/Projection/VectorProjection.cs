using System.Reflection;
using JasperFx;
using JasperFx.Core;
using System.Collections.Generic;
using System.Linq;
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
///         <c>opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(opts))</c>.
///     </para>
/// </remarks>
public abstract class VectorProjection<TId>: IProjection, IValidatedProjection<StoreOptions> where TId : notnull
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
    public virtual Table BuildTable(string schemaName) => BuildTable(schemaName, TenancyStyle.Single);

    /// <summary>
    ///     The Weasel table this projection writes to, built for the store's own tenancy
    ///     (marten#5420). <b>This is the overload to use.</b>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reads both the schema name and the tenancy style off the options, so a conjoined store
    ///         cannot get a single-tenant table by omission:
    ///         <c>opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(opts))</c>.
    ///     </para>
    /// </remarks>
    public Table BuildTable(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return BuildTable(options.Events.DatabaseSchemaName, options.Events.TenancyStyle);
    }

    /// <summary>
    ///     The table, keyed by <c>(tenant_id, id)</c> under conjoined tenancy and by <c>id</c> alone
    ///     otherwise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>The primary key is what makes the upsert safe, not just the read.</b> A stream id
    ///         is only unique per tenant under conjoined tenancy — <c>mt_streams</c> is keyed
    ///         <c>(tenant_id, id)</c> — so with <c>id</c> alone as the key, two tenants' embeddings for
    ///         the same stream collide: <c>ON CONFLICT (id)</c> makes the later write REPLACE the
    ///         earlier tenant's embedding and text rather than insert beside it. Content-hash skipping
    ///         then hides it further, because a tenant whose content hashes the same as another's is
    ///         skipped and never gets a row at all.
    ///     </para>
    ///     <para>
    ///         Following <c>StreamsTable</c>, which puts <c>tenant_id</c> first in the key for the same
    ///         reason.
    ///     </para>
    /// </remarks>
    public virtual Table BuildTable(string schemaName, TenancyStyle tenancy)
    {
        var table = new Table(new PostgresqlObjectName(schemaName, _tableName));

        if (tenancy == TenancyStyle.Conjoined)
        {
            table.AddColumn<string>("tenant_id").NotNull()
                .DefaultValueByString(StorageConstants.DefaultTenantId).AsPrimaryKey();
        }

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

    /// <summary>
    ///     Refuse a conjoined store whose projection table was built without <c>tenant_id</c>
    ///     (marten#5420).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Every failure this catches is otherwise silent</b>, which is why it is worth
    ///         failing the store build over. A conjoined store registering
    ///         <c>BuildTable(schemaName)</c> gets a table keyed on <c>id</c> alone: one tenant's search
    ///         returns another's rows, one tenant's write replaces another's through
    ///         <c>ON CONFLICT (id)</c>, and one tenant's delete removes another's. Nothing throws and
    ///         the projection reports healthy throughout.
    ///     </para>
    ///     <para>
    ///         Checked against the table the consumer actually REGISTERED rather than against the one
    ///         this class would build, because the registration is the thing that can be wrong — and
    ///         a subclass is free to override <see cref="BuildTable(string, TenancyStyle)" />. A
    ///         projection whose table was never registered at all is not reported here; that is a
    ///         different mistake and it fails loudly on the first write.
    ///     </para>
    /// </remarks>
    IEnumerable<string> IValidatedProjection<StoreOptions>.ValidateConfiguration(StoreOptions options)
    {
        if (options.Events.TenancyStyle != TenancyStyle.Conjoined)
        {
            yield break;
        }

        var registered = options.Storage.ExtendedSchemaObjects
            .OfType<Table>()
            .FirstOrDefault(x => x.Identifier.Name.EqualsIgnoreCase(_tableName));

        if (registered is null || registered.Columns.Any(x => x.Name.EqualsIgnoreCase("tenant_id")))
        {
            yield break;
        }

        yield return
            $"{GetType().FullNameInCode()} writes to '{_tableName}', which was registered without a "
            + "tenant_id column on a store using TenancyStyle.Conjoined. Every tenant's embeddings "
            + "would share one table keyed on id alone, so a search returns other tenants' rows and a "
            + "write replaces them. Register the table with "
            + "opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(opts)), which takes the "
            + "tenancy from the store.";
    }

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

        // marten#5420. The session's tenant is the right source and needs no per-event grouping:
        // ApplyAsync is called once PER TENANT with a tenant-scoped session, because the daemon
        // groups a page by TenantId and opens a session for each group before handing it over.
        // Null under single tenancy, where every statement below drops its tenant term.
        var tenantId = store.Options.Events.TenancyStyle == TenancyStyle.Conjoined
            ? operations.TenantId
            : null;

        foreach (var id in plan.Deletions)
        {
            if (tenantId is null)
            {
                operations.QueueSqlCommand($"DELETE FROM {qualifiedTable} WHERE id = ?", id);
            }
            else
            {
                operations.QueueSqlCommand(
                    $"DELETE FROM {qualifiedTable} WHERE id = ? AND tenant_id = ?", id, tenantId);
            }
        }

        var writes = await plan
            .ResolveAsync(_provider,
                (ids, token) => ReadHashesAsync(operations, qualifiedTable, tenantId, ids, token),
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
            if (tenantId is null)
            {
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
            else
            {
                // ON CONFLICT names the WHOLE key. Leaving it as (id) here would still overwrite the
                // other tenant's row -- the conflict target has to match the primary key the table
                // was built with, or the statement either errors or resolves against the wrong one.
                operations.QueueSqlCommand(
                    $"INSERT INTO {qualifiedTable} (tenant_id, id, embedding, content_text, content_hash, last_updated) "
                    + $"VALUES (?, ?, ?::vector({_provider.Dimensions}), ?, ?, now()) "
                    + "ON CONFLICT (tenant_id, id) DO UPDATE SET embedding = excluded.embedding, "
                    + "content_text = excluded.content_text, content_hash = excluded.content_hash, "
                    + "last_updated = now()",
                    tenantId,
                    write.Id,
                    new Vector(write.Embedding).ToString(),
                    write.Content,
                    write.ContentHash);
            }
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
        IDocumentOperations operations, string qualifiedTable, string? tenantId, IReadOnlyList<TId> ids,
        CancellationToken token)
    {
        var hashes = new Dictionary<TId, string>();
        if (ids.Count == 0) return hashes;

        await using var conn = operations.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();

        // marten#5420. The hash is what decides whether the model is called at all, so reading it
        // across tenants is not merely a leak: another tenant's matching hash makes this tenant's
        // write look unnecessary, and it is SKIPPED -- leaving that tenant with no row of its own and
        // nothing to report it.
        cmd.CommandText = tenantId is null
            ? $"SELECT id, content_hash FROM {qualifiedTable} WHERE id = ANY($1)"
            : $"SELECT id, content_hash FROM {qualifiedTable} WHERE id = ANY($1) AND tenant_id = $2";
        cmd.Parameters.Add(new NpgsqlParameter { Value = ids.ToArray() });
        if (tenantId is not null)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        }

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
