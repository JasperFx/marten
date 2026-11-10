#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Exceptions;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events;

/// <summary>
///     jasperfx#1047: the write surface of <see cref="IEventStoreOperations"/> (every <c>StartStream</c>,
///     <c>Append</c>, <c>AppendOptimistic</c>, <c>AppendExclusive</c>, <c>FetchForWriting</c>,
///     <c>FetchForExclusiveWriting</c>, <c>WriteToAggregate</c> and <c>WriteExclusivelyToAggregate</c>) is the
///     shared <see cref="EventOperationsBase"/>. <see cref="EventStore"/> already derives from
///     <see cref="QueryEventStore"/>, so it hosts one through <see cref="IEventOperationsHost"/> and keeps the
///     seams here. Argument validation (identity mismatch, empty ids, a stream started with no events, an
///     expected version below the events appended) is the base's, and runs before any seam.
/// </summary>
internal partial class EventStore: IEventOperationsHost
{
    private EventOperations? _operations;

    /// <summary>
    ///     Scoped to this instance's session, so to its tenant: a ForTenant session has an EventStore of its own.
    /// </summary>
    public EventOperationsBase Operations => _operations ??= new EventOperations(this);

    private sealed class EventOperations(EventStore parent): EventOperationsBase
    {
        protected override StreamIdentity StreamIdentity => parent._store.Events.StreamIdentity;

        // A null aggregate type is an overload that declares none, which is what
        // UseMandatoryStreamTypeDeclaration refuses
        protected override StreamAction StartStreamCore(Type? aggregateType, Guid id, object[] events)
        {
            assertStreamTypeDeclared(aggregateType);
            return parent._store.Events.StartStream(parent._session, aggregateType, id, events);
        }

        protected override StreamAction StartStreamCore(Type? aggregateType, string streamKey, object[] events)
        {
            assertStreamTypeDeclared(aggregateType);
            return parent._store.Events.StartStream(parent._session, streamKey, aggregateType, events);
        }

        protected override StreamAction AppendCore(Guid stream, object[] events)
            => parent._store.Events.Append(parent._session, stream, events);

        protected override StreamAction AppendCore(string stream, object[] events)
            => parent._store.Events.Append(parent._session, stream, events);

        protected override Task<long> ReadStreamVersionCore<TId>(TId stream, bool exclusive, CancellationToken token)
            => parent.readVersionForAppendAsync(stream, exclusive, token);

        protected override Task<IEventStream<T>> FetchForWritingCore<T, TId>(TId id, long? expectedVersion,
            bool exclusive, CancellationToken cancellation)
        {
            var plan = parent.FindFetchPlan<T, TId>();

            return expectedVersion.HasValue
                ? plan.FetchForWriting(parent._session, id, expectedVersion.Value, cancellation)
                : plan.FetchForWriting(parent._session, id, exclusive, cancellation);
        }

        protected override Task SaveChangesCore(CancellationToken cancellation)
            => parent._session.SaveChangesAsync(cancellation);

        private void assertStreamTypeDeclared(Type? aggregateType)
        {
            if (aggregateType == null && parent._store.Events.UseMandatoryStreamTypeDeclaration)
            {
                throw new StreamTypeMissingException();
            }
        }
    }

    private async Task<long> readVersionForAppendAsync<TId>(TId stream, bool exclusive, CancellationToken token)
        where TId : notnull
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        if (!exclusive)
        {
            return await readVersionFromExistingStream(stream, false, token).ConfigureAwait(false);
        }

        try
        {
            await _session.BeginTransactionAsync(token).ConfigureAwait(false);

            return await readVersionFromExistingStream(stream, true, token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not StreamLockedException and not NonExistentStreamException)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage) || e.InnerException is NpgsqlException
                {
                    SqlState: PostgresErrorCodes.InFailedSqlTransaction
                })
            {
                throw new StreamLockedException(stream, e.InnerException!);
            }

            throw;
        }
    }

    private Task<long> readVersionFromExistingStream<TId>(TId stream, bool forUpdate, CancellationToken token)
        where TId : notnull
    {
        return stream is Guid id
            ? readVersionFromExistingStream(id, forUpdate, token)
            : readVersionFromExistingStream((string)(object)stream, forUpdate, token);
    }
}
