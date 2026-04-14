using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using System.Collections.Generic;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
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
        _isConjoined = events.TenancyStyle == Storage.TenancyStyle.Conjoined;
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
            var newId = Guid.NewGuid();
            var action = _events.StartEmptyStream(session, newId);
            action.AggregateType = typeof(TDoc);
            action.ExpectedVersionOnServer = 0;
            return new EventStream<TDoc>(session, _events, newId, default, cancellation, action);
        }
        else
        {
            var newKey = Guid.NewGuid().ToString();
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
        if (session.Options.Events.UseIdentityMapForAggregates)
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

        if (document != null && session.Options.Events.UseIdentityMapForAggregates)
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
        if (session.Options.Events.UseIdentityMapForAggregates)
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

        await using var docReader =
            await session.ExecuteReaderAsync(docBuilder.Compile(), cancellation).ConfigureAwait(false);
        var document = await handler.HandleAsync(docReader, session, cancellation).ConfigureAwait(false);

        if (document != null && session.Options.Events.UseIdentityMapForAggregates)
        {
            session.StoreDocumentInItemMap(streamKey, document);
        }

        return document;
    }

    private async Task<TDoc?> AggregateFromEventsAsync(DocumentSessionBase session, string streamKey, CancellationToken cancellation)
    {
        var events = await session.Events.FetchStreamAsync(streamKey, token: cancellation).ConfigureAwait(false);
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

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
        return await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
    }

    internal class NaturalKeyQueryHandler: IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey> _parent;
        private readonly TNaturalKey _id;
        private readonly long _expectedVersion;
        private readonly bool _forUpdate;
        private readonly bool _checkVersion;

        public NaturalKeyQueryHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey> parent, TNaturalKey id,
            long expectedVersion, bool forUpdate, bool checkVersion)
        {
            _parent = parent;
            _id = id;
            _expectedVersion = expectedVersion;
            _forUpdate = forUpdate;
            _checkVersion = checkVersion;
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            var innerValue = _parent._naturalKey.Unwrap(_id)!;
            _parent.BuildNaturalKeyToStreamQuery((BatchBuilder)builder, innerValue, _forUpdate);
        }

        public async Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IMartenSession session,
            CancellationToken token)
        {
            var documentSession = (DocumentSessionBase)session;

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (_checkVersion && _expectedVersion != 0)
                {
                    throw new ConcurrencyException(
                        $"Expected the existing version to be {_expectedVersion}, but was 0",
                        typeof(TDoc), _id);
                }

                return _parent.CreateNewStream<TDoc>(documentSession, token);
            }

            var version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);

            if (_checkVersion && _expectedVersion != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {_expectedVersion}, but was {version}",
                    typeof(TDoc), _id);
            }

            // Extract stream identity from reader before it's closed by batch framework
            var streamIdentity = _parent._events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, token).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            // Close the reader result set before opening second reader
            while (await reader.ReadAsync(token).ConfigureAwait(false)) { }

            if (_parent._events.StreamIdentity == StreamIdentity.AsGuid)
            {
                return await _parent.FetchByStreamId(documentSession, (Guid)streamIdentity, version, token)
                    .ConfigureAwait(false);
            }
            else
            {
                return await _parent.FetchByStreamKey(documentSession, (string)streamIdentity, version, token)
                    .ConfigureAwait(false);
            }
        }

        public IEventStream<TDoc> Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    internal class NaturalKeyReadQueryHandler: IQueryHandler<TDoc?>
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey> _parent;
        private readonly TNaturalKey _id;

        public NaturalKeyReadQueryHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey> parent, TNaturalKey id)
        {
            _parent = parent;
            _id = id;
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            var innerValue = _parent._naturalKey.Unwrap(_id)!;
            _parent.BuildNaturalKeyToStreamQuery((BatchBuilder)builder, innerValue, false);
        }

        public async Task<TDoc?> HandleAsync(DbDataReader reader, IMartenSession session,
            CancellationToken token)
        {
            var documentSession = (DocumentSessionBase)session;

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return default;
            }

            // Extract stream identity before closing reader
            var streamIdentity = _parent._events.StreamIdentity == StreamIdentity.AsGuid
                ? await reader.GetFieldValueAsync<Guid>(1, token).ConfigureAwait(false)
                : (object)await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false)) { }

            if (_parent._events.StreamIdentity == StreamIdentity.AsGuid)
            {
                return await _parent.FetchDocByGuid(documentSession, (Guid)streamIdentity, token)
                    .ConfigureAwait(false);
            }
            else
            {
                return await _parent.FetchDocByString(documentSession, (string)streamIdentity, token)
                    .ConfigureAwait(false);
            }
        }

        public TDoc? Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
