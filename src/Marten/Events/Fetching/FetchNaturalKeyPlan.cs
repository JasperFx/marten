using System;
using JasperFx.Core;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Core.Reflection;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

/// <summary>
/// Fetch plan that resolves a natural key to a stream identity, then fetches the aggregate.
/// The natural key is first looked up in the mt_natural_key_{type} table to resolve to a
/// stream id (Guid) or stream key (string), then the document is loaded by stream identity.
/// </summary>
internal class FetchNaturalKeyPlan<TDoc, TNaturalKey>: IAggregateFetchPlan<TDoc, TNaturalKey>
    where TDoc : class where TNaturalKey : notnull
{
    private readonly EventGraph _events;
    private readonly NaturalKeyDefinition _naturalKey;
    private readonly string _naturalKeyTableName;
    private readonly string _streamIdColumn;
    private readonly bool _isConjoined;
    private readonly bool _isGlobal;
    private readonly StoreOptions _options;

    public FetchNaturalKeyPlan(EventGraph events, NaturalKeyDefinition naturalKey,
        ProjectionLifecycle lifecycle, StoreOptions options)
    {
        _events = events;
        _naturalKey = naturalKey;
        _options = options;
        Lifecycle = lifecycle;

        _naturalKeyTableName =
            $"{events.DatabaseSchemaName}.mt_natural_key_{naturalKey.AggregateType.Name.ToLowerInvariant()}";
        _streamIdColumn = events.StreamIdentity == StreamIdentity.AsGuid ? "stream_id" : "stream_key";
        _isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        _isGlobal = events.GlobalAggregates.Contains(typeof(TDoc));
    }

    public ProjectionLifecycle Lifecycle { get; }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TNaturalKey id,
        bool forUpdate, CancellationToken cancellation = default)
    {
        await EnsureStorageExists(session, cancellation).ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var innerValue = _naturalKey.Unwrap(id)!;

        var builder = new BatchBuilder { TenantId = session.TenantId };
        BuildNaturalKeyToStreamQuery(builder, innerValue, forUpdate);

        try
        {
            // Read the natural key lookup result and extract stream identity,
            // then close the reader BEFORE opening a second reader for the document
            long version = 0;
            object? streamIdentity = null;
            bool found = false;

            await using (var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                {
                    version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
                    streamIdentity = _events.StreamIdentity == StreamIdentity.AsGuid
                        ? await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false)
                        : (object)await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
                    found = true;
                }
            }

            if (!found)
            {
                return CreateNewStream<TDoc>(session, cancellation);
            }

            if (_events.StreamIdentity == StreamIdentity.AsGuid)
            {
                return await FetchByStreamId(session, (Guid)streamIdentity!, version, cancellation).ConfigureAwait(false);
            }
            else
            {
                return await FetchByStreamKey(session, (string)streamIdentity!, version, cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TNaturalKey id,
        long expectedStartingVersion, CancellationToken cancellation = default)
    {
        await EnsureStorageExists(session, cancellation).ConfigureAwait(false);

        var innerValue = _naturalKey.Unwrap(id)!;

        var builder = new BatchBuilder { TenantId = session.TenantId };
        BuildNaturalKeyToStreamQuery(builder, innerValue, false);

        long version = 0;
        object? streamIdentity = null;
        bool found = false;

        await using (var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                if (expectedStartingVersion != 0)
                {
                    throw new ConcurrencyException(
                        $"Expected the existing version to be {expectedStartingVersion}, but was 0",
                        typeof(TDoc), id);
                }

                return CreateNewStream<TDoc>(session, cancellation);
            }

            version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);

            if (expectedStartingVersion != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {expectedStartingVersion}, but was {version}",
                    typeof(TDoc), id);
            }

            streamIdentity = _events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
            found = true;
        }

        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            return await FetchByStreamId(session, (Guid)streamIdentity!, version, cancellation).ConfigureAwait(false);
        }
        else
        {
            return await FetchByStreamKey(session, (string)streamIdentity!, version, cancellation).ConfigureAwait(false);
        }
    }

    public async ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TNaturalKey id,
        CancellationToken cancellation)
    {
        await EnsureStorageExists(session, cancellation).ConfigureAwait(false);

        var innerValue = _naturalKey.Unwrap(id)!;

        var builder = new BatchBuilder { TenantId = session.TenantId };
        BuildNaturalKeyToStreamQuery(builder, innerValue, false);

        object? streamIdentity = null;

        await using (var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                return default;
            }

            // Read stream identity column, skip version (index 0)
            streamIdentity = _events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
        }

        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            return await FetchDocByGuid(session, (Guid)streamIdentity!, cancellation).ConfigureAwait(false);
        }
        else
        {
            return await FetchDocByString(session, (string)streamIdentity!, cancellation).ConfigureAwait(false);
        }
    }

    public async ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TNaturalKey id,
        CancellationToken cancellation)
    {
        // For natural keys, we cannot reliably find pending events because they are
        // tracked by stream ID (Guid/string), not by natural key. The natural key mapping
        // is typically created by the inline projection itself, which hasn't run yet for
        // uncommitted events. Fall back to FetchForReading.
        return await FetchForReading(session, id, cancellation).ConfigureAwait(false);
    }

    public async Task<bool> StreamForReading(DocumentSessionBase session, TNaturalKey id, Stream destination,
        CancellationToken cancellation)
    {
        await EnsureStorageExists(session, cancellation).ConfigureAwait(false);

        var innerValue = _naturalKey.Unwrap(id)!;

        var builder = new BatchBuilder { TenantId = session.TenantId };
        BuildNaturalKeyToStreamQuery(builder, innerValue, false);

        object? streamIdentity = null;

        await using (var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                return false;
            }

            streamIdentity = _events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
        }

        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var storage = _options.ResolveCorrectedDocumentStorage<TDoc, Guid>(session.TrackingMode);
            var command = storage.BuildLoadCommand((Guid)streamIdentity!, session.TenantId);
            return await session.StreamOne(command, destination, cancellation).ConfigureAwait(false);
        }
        else
        {
            var storage = _options.ResolveCorrectedDocumentStorage<TDoc, string>(session.TrackingMode);
            var command = storage.BuildLoadCommand((string)streamIdentity!, session.TenantId);
            return await session.StreamOne(command, destination, cancellation).ConfigureAwait(false);
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TNaturalKey id,
        long expectedStartingVersion)
    {
        session.AssertIsDocumentSession();
        return new NaturalKeyQueryHandler(this, id, expectedStartingVersion, false, true);
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TNaturalKey id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        return new NaturalKeyQueryHandler(this, id, 0, forUpdate, false);
    }

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TNaturalKey id)
    {
        return new NaturalKeyReadQueryHandler(this, id);
    }

    private async Task EnsureStorageExists(DocumentSessionBase session, CancellationToken cancellation)
    {
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            _events.EnsureAsGuidStorage(session);
        }
        else
        {
            _events.EnsureAsStringStorage(session);
        }

        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);
    }

    internal void BuildNaturalKeyToStreamQuery(BatchBuilder builder, object innerValue, bool forUpdate)
    {
        builder.Append("select s.version, nk.");
        builder.Append(_streamIdColumn);
        builder.Append(" from ");
        builder.Append(_naturalKeyTableName);
        builder.Append(" nk inner join ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_streams s on s.id = nk.");
        builder.Append(_streamIdColumn);
        builder.Append(" where nk.natural_key_value = ");
        builder.AppendParameter(innerValue);
        builder.Append(" and nk.is_archived = false");

        // #5344: archival is a property of the *stream*, and nothing on the write side copies it
        // onto the lookup row -- ArchiveStream() and the Archived event both go through
        // mt_archive_stream, which has never touched mt_natural_key_X. Reading the flag off the
        // stream we are already joined to is what Fisher does, and it needs no schema change, no
        // migration of existing lookup tables, and covers every route into the archive (the explicit
        // call, the Archived marker event, and the daemon) with one predicate. nk.is_archived above
        // is kept because it is genuinely maintained under UseArchivedStreamPartitioning, where
        // mt_archive_stream / mt_unarchive_stream copy the lookup row between partitions with its stream
        // (marten#5476).
        builder.Append(" and s.is_archived = false");

        if (_isConjoined && !_isGlobal)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(builder.TenantId);

            builder.Append(" and nk.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update of s");
        }
    }

    private async Task<IEventStream<TDoc>> ReadStreamFromNaturalKey(DocumentSessionBase session,
        TNaturalKey naturalKey, DbDataReader reader, CancellationToken cancellation)
    {
        if (!await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            return CreateNewStream<TDoc>(session, cancellation);
        }

        var version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
        return await LoadDocumentAndBuildStream(session, reader, version, cancellation).ConfigureAwait(false);
    }

    private async Task<IEventStream<TDoc>> LoadDocumentAndBuildStream(DocumentSessionBase session,
        DbDataReader reader, long version, CancellationToken cancellation)
    {
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var streamId = await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false);
            return await FetchByStreamId(session, streamId, version, cancellation).ConfigureAwait(false);
        }
        else
        {
            var streamKey = await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
            return await FetchByStreamKey(session, streamKey, version, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<TDoc?> LoadDocumentByStreamIdentity(DocumentSessionBase session, DbDataReader reader,
        CancellationToken cancellation)
    {
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var streamId = await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false);
            return await FetchDocByGuid(session, streamId, cancellation).ConfigureAwait(false);
        }
        else
        {
            var streamKey = await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
            return await FetchDocByString(session, streamKey, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<bool> StreamDocumentByStreamIdentity(DocumentSessionBase session, DbDataReader reader,
        Stream destination, CancellationToken cancellation)
    {
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var streamId = await reader.GetFieldValueAsync<Guid>(1, cancellation).ConfigureAwait(false);
            var storage = _options.ResolveCorrectedDocumentStorage<TDoc, Guid>(session.TrackingMode);
            var command = storage.BuildLoadCommand(streamId, session.TenantId);
            return await session.StreamOne(command, destination, cancellation).ConfigureAwait(false);
        }
        else
        {
            var streamKey = await reader.GetFieldValueAsync<string>(1, cancellation).ConfigureAwait(false);
            var storage = _options.ResolveCorrectedDocumentStorage<TDoc, string>(session.TrackingMode);
            var command = storage.BuildLoadCommand(streamKey, session.TenantId);
            return await session.StreamOne(command, destination, cancellation).ConfigureAwait(false);
        }
    }

    private IEventStream<TDoc> CreateNewStream<T>(DocumentSessionBase session, CancellationToken cancellation)
        where T : class
    {
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var newId = SequentialGuid.NextPostgresOrder();
            var action = _events.StartEmptyStream(session, newId);
            action.AggregateType = typeof(TDoc);
            action.ExpectedVersionOnServer = 0;
            return new EventStream<TDoc>(session, _events, newId, default, cancellation, action);
        }
        else
        {
            var newKey = SequentialGuid.NextPostgresOrder().ToString();
            var action = _events.StartEmptyStream(session, newKey);
            action.AggregateType = typeof(TDoc);
            action.ExpectedVersionOnServer = 0;
            return new EventStream<TDoc>(session, _events, newKey, default, cancellation, action);
        }
    }

    private async Task<IEventStream<TDoc>> FetchByStreamId(DocumentSessionBase session,
        Guid streamId, long version, CancellationToken cancellation)
    {
        TDoc? document;
        if (Lifecycle == ProjectionLifecycle.Live)
        {
            document = await AggregateFromEventsAsync(session, streamId, cancellation).ConfigureAwait(false);
        }
        else
        {
            document = await LoadDocumentById(session, streamId, cancellation).ConfigureAwait(false);
        }

        if (version == 0)
        {
            var action = _events.StartEmptyStream(session, streamId);
            action.AggregateType = typeof(TDoc);
            action.ExpectedVersionOnServer = 0;
            return new EventStream<TDoc>(session, _events, streamId, document, cancellation, action);
        }
        else
        {
            var action = session.Events.Append(streamId);
            action.ExpectedVersionOnServer = version;
            return new EventStream<TDoc>(session, _events, streamId, document, cancellation, action);
        }
    }

    private async Task<TDoc?> LoadDocumentById(DocumentSessionBase session, Guid streamId, CancellationToken cancellation)
    {
        IDocumentStorage<TDoc, Guid> storage;
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            storage = _options.ResolveCorrectedDocumentStorage<TDoc, Guid>(DocumentTracking.IdentityOnly);
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = _options.ResolveCorrectedDocumentStorage<TDoc, Guid>(session.TrackingMode);
        }

        var docBuilder = new BatchBuilder { TenantId = session.TenantId };
        docBuilder.Append(";");
        var handler = new LoadByIdHandler<TDoc, Guid>(storage, streamId);
        handler.ConfigureCommand(docBuilder, session);

        await using var docReader =
            await session.ExecuteReaderAsync(docBuilder.Compile(), cancellation).ConfigureAwait(false);
        var document = await handler.HandleAsync(docReader, session, cancellation).ConfigureAwait(false);

        if (document != null && ((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            session.StoreDocumentInItemMap(streamId, document);
        }

        return document;
    }

    private async Task<TDoc?> AggregateFromEventsAsync(DocumentSessionBase session, Guid streamId, CancellationToken cancellation)
    {
        var events = await session.Events.FetchStreamAsync(streamId, token: cancellation).ConfigureAwait(false);
        if (events.Count == 0) return default;

        var aggregator = _options.Projections.AggregatorFor<TDoc>();
        return await aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);
    }

    private async Task<IEventStream<TDoc>> FetchByStreamKey(DocumentSessionBase session,
        string streamKey, long version, CancellationToken cancellation)
    {
        TDoc? document;
        if (Lifecycle == ProjectionLifecycle.Live)
        {
            document = await AggregateFromEventsAsync(session, streamKey, cancellation).ConfigureAwait(false);
        }
        else
        {
            document = await LoadDocumentByKey(session, streamKey, cancellation).ConfigureAwait(false);
        }

        if (version == 0)
        {
            var action = _events.StartEmptyStream(session, streamKey);
            action.AggregateType = typeof(TDoc);
            action.ExpectedVersionOnServer = 0;
            return new EventStream<TDoc>(session, _events, streamKey, document, cancellation, action);
        }
        else
        {
            var action = session.Events.Append(streamKey);
            action.ExpectedVersionOnServer = version;
            return new EventStream<TDoc>(session, _events, streamKey, document, cancellation, action);
        }
    }

    private async Task<TDoc?> LoadDocumentByKey(DocumentSessionBase session, string streamKey, CancellationToken cancellation)
    {
        IDocumentStorage<TDoc, string> storage;
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            storage = _options.ResolveCorrectedDocumentStorage<TDoc, string>(DocumentTracking.IdentityOnly);
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = _options.ResolveCorrectedDocumentStorage<TDoc, string>(session.TrackingMode);
        }

        var docBuilder = new BatchBuilder { TenantId = session.TenantId };
        docBuilder.Append(";");
        var handler = new LoadByIdHandler<TDoc, string>(storage, streamKey);
        handler.ConfigureCommand(docBuilder, session);

        TDoc? document;
        await using (var docReader =
                     await session.ExecuteReaderAsync(docBuilder.Compile(), cancellation).ConfigureAwait(false))
        {
            document = await handler.HandleAsync(docReader, session, cancellation).ConfigureAwait(false);
        }

        document ??= await startFromColdAsync(session, storage, streamKey, cancellation).ConfigureAwait(false);

        if (document != null && ((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            session.StoreDocumentInItemMap(streamKey, document);
        }

        return document;
    }

    private async Task<TDoc?> AggregateFromEventsAsync(DocumentSessionBase session, string streamKey, CancellationToken cancellation)
    {
        // With a cold store, the cold part of the stream first, as FetchLivePlan folds it, and the cold version is
        // remembered so the commit fails if the stream goes cold past it in between
        var (events, coldVersion) = await ((QueryEventStore)session.Events)
            .FetchStreamToFoldAsync(streamKey, cancellation).ConfigureAwait(false);
        if (_events.ColdEventStore != null)
        {
            session.RememberColdVersion(session.TenantId, streamKey, coldVersion);
        }

        if (events.Count == 0) return default;

        var aggregator = _options.Projections.AggregatorFor<TDoc>();
        return await aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);
    }

    internal async Task<TDoc?> FetchDocByGuid(DocumentSessionBase session, Guid streamId,
        CancellationToken cancellation)
    {
        if (Lifecycle == ProjectionLifecycle.Live)
        {
            return await AggregateFromEventsAsync(session, streamId, cancellation).ConfigureAwait(false);
        }

        var storage = _options.ResolveCorrectedDocumentStorage<TDoc, Guid>(session.TrackingMode);
        var builder = new BatchBuilder { TenantId = session.TenantId };
        builder.Append(";");
        var handler = new LoadByIdHandler<TDoc, Guid>(storage, streamId);
        handler.ConfigureCommand(builder, session);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
        return await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
    }

    internal async Task<TDoc?> FetchDocByString(DocumentSessionBase session, string streamKey,
        CancellationToken cancellation)
    {
        if (Lifecycle == ProjectionLifecycle.Live)
        {
            return await AggregateFromEventsAsync(session, streamKey, cancellation).ConfigureAwait(false);
        }

        var storage = _options.ResolveCorrectedDocumentStorage<TDoc, string>(session.TrackingMode);
        var builder = new BatchBuilder { TenantId = session.TenantId };
        builder.Append(";");
        var handler = new LoadByIdHandler<TDoc, string>(storage, streamKey);
        handler.ConfigureCommand(builder, session);

        TDoc? document;
        await using (var reader =
                     await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        }

        return document ?? await startFromColdAsync(session, storage, streamKey, cancellation).ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (JasperFx.Events aggregator graph, CloseAndBuildAs). Document/event/projection types flow in from StoreOptions and projection registration and are preserved per the AOT publishing guide.")]
    private IAggregator<TDoc, string, IQuerySession> aggregatorFor(IDocumentStorage<TDoc, string> storage)
    {
        var raw = _options.Projections.AggregatorFor<TDoc>();
        return raw as IAggregator<TDoc, string, IQuerySession>
               ?? typeof(IdentityForwardingAggregator<,,,>)
                   .CloseAndBuildAs<IAggregator<TDoc, string, IQuerySession>>(raw, storage, typeof(TDoc),
                       storage.IdType, typeof(string), typeof(IQuerySession));
    }

    /// <summary>
    ///     For an Inline or Async aggregate whose stored document is missing: where it starts on a stream with a cold
    ///     part, as the plans by stream identity start (<see cref="FetchInlinedPlan{TDoc,TId}" />,
    ///     <see cref="FetchAsyncPlan{TDoc,TId}" />). Inline, only when nothing warm follows the cold part, because a
    ///     warm event after it was projected already. Async, the warm events after the cold part are folded onto the
    ///     start. Null without a cold store, without a cold part, or for a projection that ignores cold streams.
    /// </summary>
    private async Task<TDoc?> startFromColdAsync(DocumentSessionBase session, IDocumentStorage<TDoc, string> storage,
        string streamKey, CancellationToken token)
    {
        if (_events.ColdEventStore == null) return null;
        if (ColdAggregationStart.For<TDoc, string>(_events, () => aggregatorFor(storage)) is not { } cold) return null;

        var async = Lifecycle == ProjectionLifecycle.Async;
        var start = await cold.StartFetchAsync(session, streamKey, streamKey, storage, async, token)
            .ConfigureAwait(false);
        if (start is not { } found) return null;

        var document = found.Start;
        if (async)
        {
            var warm = await session.Events.FetchStreamAsync(streamKey, fromVersion: found.ColdVersion + 1, token: token)
                .ConfigureAwait(false);
            if (warm.Count > 0)
            {
                document = await aggregatorFor(storage).BuildAsync(warm, session, document, streamKey, storage, token)
                    .ConfigureAwait(false);
            }
        }

        return document;
    }

    /// <remarks>
    ///     The second read, of the aggregate, runs in <see cref="FinishAsync" />, after the batch's reader is closed.
    /// </remarks>
    internal class NaturalKeyQueryHandler: IFinishesAfterTheReader<IEventStream<TDoc>>, IExclusiveFetchHandler
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey> _parent;
        private readonly TNaturalKey _id;
        private readonly long _expectedVersion;
        private readonly bool _forUpdate;
        private readonly bool _checkVersion;
        private bool _found;
        private long _version;
        private object? _streamIdentity;

        public NaturalKeyQueryHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey> parent, TNaturalKey id,
            long expectedVersion, bool forUpdate, bool checkVersion)
        {
            _parent = parent;
            _id = id;
            _expectedVersion = expectedVersion;
            _forUpdate = forUpdate;
            _checkVersion = checkVersion;
        }

        public object? ExclusiveStreamId => _forUpdate ? _id : null;

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            var innerValue = _parent._naturalKey.Unwrap(_id)!;
            _parent.BuildNaturalKeyToStreamQuery((BatchBuilder)builder, innerValue, _forUpdate);
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            _found = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!_found)
            {
                if (_checkVersion && _expectedVersion != 0)
                {
                    throw new ConcurrencyException(
                        $"Expected the existing version to be {_expectedVersion}, but was 0",
                        typeof(TDoc), _id);
                }

                return;
            }

            _version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);

            if (_checkVersion && _expectedVersion != _version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {_expectedVersion}, but was {_version}",
                    typeof(TDoc), _id);
            }

            _streamIdentity = _parent._events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, token).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false)) { }
        }

        public async Task<IEventStream<TDoc>> FinishAsync(IStorageSession session, CancellationToken token)
        {
            var documentSession = (DocumentSessionBase)session;
            if (!_found)
            {
                return _parent.CreateNewStream<TDoc>(documentSession, token);
            }

            if (_parent._events.StreamIdentity == StreamIdentity.AsGuid)
            {
                return await _parent.FetchByStreamId(documentSession, (Guid)_streamIdentity!, _version, token)
                    .ConfigureAwait(false);
            }

            return await _parent.FetchByStreamKey(documentSession, (string)_streamIdentity!, _version, token)
                .ConfigureAwait(false);
        }

        public async Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session,
            CancellationToken token)
        {
            await ReadAsync(reader, session, token).ConfigureAwait(false);
            return await FinishAsync(session, token).ConfigureAwait(false);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    /// <remarks>
    ///     The second read, of the aggregate, runs in <see cref="FinishAsync" />, after the batch's reader is closed.
    /// </remarks>
    internal class NaturalKeyReadQueryHandler: IFinishesAfterTheReader<TDoc?>
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey> _parent;
        private readonly TNaturalKey _id;
        private object? _streamIdentity;

        public NaturalKeyReadQueryHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey> parent, TNaturalKey id)
        {
            _parent = parent;
            _id = id;
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            var innerValue = _parent._naturalKey.Unwrap(_id)!;
            _parent.BuildNaturalKeyToStreamQuery((BatchBuilder)builder, innerValue, false);
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                _streamIdentity = null;
                return;
            }

            _streamIdentity = _parent._events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, token).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false)) { }
        }

        public async Task<TDoc?> FinishAsync(IStorageSession session, CancellationToken token)
        {
            var documentSession = (DocumentSessionBase)session;
            return _streamIdentity switch
            {
                null => default,
                Guid streamId => await _parent.FetchDocByGuid(documentSession, streamId, token).ConfigureAwait(false),
                _ => await _parent.FetchDocByString(documentSession, (string)_streamIdentity, token)
                    .ConfigureAwait(false)
            };
        }

        public async Task<TDoc?> HandleAsync(DbDataReader reader, IStorageSession session,
            CancellationToken token)
        {
            await ReadAsync(reader, session, token).ConfigureAwait(false);
            return await FinishAsync(session, token).ConfigureAwait(false);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
