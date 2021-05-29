using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Progress;
using Marten.Events.TestSupport;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Weasel.Postgresql;

#nullable enable
namespace Marten
{
    /// <summary>
    /// Access to advanced, rarely used features of IDocumentStore
    /// </summary>
    public class AdvancedOperations
    {
        private readonly DocumentStore _store;

        internal AdvancedOperations(DocumentStore store)
        {
            _store = store;
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(long floor)
        {
            return _store.Tenancy.Default.ResetHiloSequenceFloor<T>(floor);
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(string tenantId, long floor)
        {
            return _store.Tenancy[tenantId].ResetHiloSequenceFloor<T>(floor);
        }

        /// <summary>
        ///     Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean => _store.Tenancy.Cleaner;

        public ISerializer Serializer => _store.Serializer;

        /// <summary>
        /// Fetch the current size of the event store tables, including the current value
        /// of the event sequence number
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<EventStoreStatistics> FetchEventStoreStatistics(CancellationToken token = default)
        {
            var sql = $@"
select count(*) from {_store.Events.DatabaseSchemaName}.mt_events;
select count(*) from {_store.Events.DatabaseSchemaName}.mt_streams;
select last_value from {_store.Events.DatabaseSchemaName}.mt_events_sequence;
";

            await _store.Tenancy.Default.EnsureStorageExistsAsync(typeof(IEvent), token);

            var statistics = new EventStoreStatistics();

            await using var conn = _store.Tenancy.Default.CreateConnection();
            await conn.OpenAsync(token);

            await using var reader = await conn.CreateCommand(sql).ExecuteReaderAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.EventCount = await reader.GetFieldValueAsync<long>(0, token);
            }

            await reader.NextResultAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.StreamCount = await reader.GetFieldValueAsync<long>(0, token);
            }

            await reader.NextResultAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.EventSequenceNumber = await reader.GetFieldValueAsync<long>(0, token);
            }

            return statistics;
        }

        /// <summary>
        /// Check the current progress of all asynchronous projections
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(CancellationToken token = default)
        {
            await _store.Tenancy.Default.EnsureStorageExistsAsync(typeof(IEvent), token);

            var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(new ProjectionProgressStatement(_store.Events),
                new ShardStateSelector());

            await using var session = (QuerySession)_store.QuerySession();
            return await session.ExecuteHandlerAsync(handler, token);
        }

        /// <summary>
        /// Check the current progress of a single projection or projection shard
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<long> ProjectionProgressFor(ShardName name, CancellationToken token = default)
        {
            await _store.Tenancy.Default.EnsureStorageExistsAsync(typeof(IEvent), token);

            var statement = new ProjectionProgressStatement(_store.Events)
            {
                Name = name
            };

            var handler = new OneResultHandler<ShardState>(statement,
                new ShardStateSelector(), true, false);

            await using var session = (QuerySession)_store.QuerySession();

            var progress = await session.ExecuteHandlerAsync(handler, token);

            return progress?.Sequence ?? 0;
        }



        /// <summary>
        /// Access the generated source code Marten is using for a given
        /// document type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public IDocumentSourceCode SourceCodeForDocumentType(Type type)
        {
            var loader = typeof(DocumentSourceCodeLoader<>)
                .CloseAndBuildAs<IDocumentSourceCodeLoader>(type);

            return loader.Load(_store.Options.Providers);
        }

        /// <summary>
        /// See the code that Marten generates for the current configuration of the
        /// Event Store
        /// </summary>
        /// <returns></returns>
        public string SourceCodeForEventStore()
        {
            return _store.Options.Providers.StorageFor<IEvent>().SourceCode;
        }

        internal interface IDocumentSourceCodeLoader
        {
            IDocumentSourceCode Load(IProviderGraph providers);
        }

        internal class DocumentSourceCodeLoader<T>: IDocumentSourceCodeLoader where T : notnull
        {
            public IDocumentSourceCode Load(IProviderGraph providers)
            {
                return providers.StorageFor<T>();
            }
        }


        /// <summary>
        /// Marten's built in test support for event projections. Only use this in testing as
        /// it will delete existing event and projected aggregate data
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public Task EventProjectionScenario(Action<ProjectionScenario> configuration)
        {
            var scenario = new ProjectionScenario(_store);
            configuration(scenario);

            return scenario.Execute();
        }

    }
}
