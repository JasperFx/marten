using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events
{
    internal class FetchInlinedPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
    {
        private readonly EventGraph _events;
        private readonly IEventIdentityStrategy<TId> _identityStrategy;
        private readonly IDocumentStorage<TDoc, TId> _storage;

        internal FetchInlinedPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy, IDocumentStorage<TDoc, TId> storage)
        {
            _events = events;
            _identityStrategy = identityStrategy;
            _storage = storage;
        }

        public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate, CancellationToken cancellation = default)
        {
            await _identityStrategy.EnsureAggregateStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
            await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

            if (forUpdate)
            {
                await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
            }

            var command = _identityStrategy.BuildCommandForReadingVersionForStream(id, forUpdate);
            var builder = new CommandBuilder(command);
            builder.Append(";");

            var handler = new LoadByIdHandler<TDoc, TId>(_storage, id);
            handler.ConfigureCommand(builder, session);

            long version = 0;
            try
            {
                using var reader = await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                {
                    version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
                }

                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

                return version == 0
                    ? _identityStrategy.StartStream<TDoc>(document, session, id, cancellation)
                    : _identityStrategy.AppendToStream<TDoc>(document, session, id, version, cancellation);

            }
            catch (Exception e)
            {
                if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
                {
                    throw new StreamLockedException(id, e.InnerException);
                }

                throw;
            }
        }

        public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion, CancellationToken cancellation = default)
        {
            await _identityStrategy.EnsureAggregateStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
            await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

            var command = _identityStrategy.BuildCommandForReadingVersionForStream(id, false);
            var builder = new CommandBuilder(command);
            builder.Append(";");

            var handler = new LoadByIdHandler<TDoc, TId>(_storage, id);
            handler.ConfigureCommand(builder, session);

            long version = 0;
            try
            {
                using var reader = await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                {
                    version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
                }

                if (expectedStartingVersion != version)
                {
                    throw new ConcurrencyException(
                        $"Expected the existing version to be {expectedStartingVersion}, but was {version}",
                        typeof(TDoc), id);
                }

                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

                return version == 0
                    ? _identityStrategy.StartStream<TDoc>(document, session, id, cancellation)
                    : _identityStrategy.AppendToStream<TDoc>(document, session, id, version, cancellation);

            }
            catch (Exception e)
            {
                if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
                {
                    throw new StreamLockedException(id, e.InnerException);
                }

                throw;
            }
        }


    }
}
