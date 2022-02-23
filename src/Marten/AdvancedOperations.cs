using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Progress;
using Marten.Events.TestSupport;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.CodeAnalysis;
using Weasel.Core;
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
        /// Deletes all current document and event data, then (re)applies the configured
        /// initial data
        /// </summary>
        public async Task ResetAllData()
        {
            foreach (var database in _store.Tenancy.BuildDatabases().OfType<IMartenDatabase>())
            {
                await database.DeleteAllDocumentsAsync().ConfigureAwait(false);
                await database.DeleteAllEventDataAsync().ConfigureAwait(false);
            }


            foreach (var initialData in _store.Options.InitialData)
            {
                await initialData.Populate(_store).ConfigureAwait(false);
            }
        }

        private IEnumerable<IMartenDatabase> databases(string? tenantId)
        {
            if (tenantId.IsEmpty())
            {
                yield return _store.Tenancy.Default.Database;
            }
            else
            {
                foreach (var database in _store.Tenancy.BuildDatabases().OfType<IMartenDatabase>())
                {
                    yield return database;
                }
            }
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        /// <param name="tenantId">If supplied, this will only apply to the database holding the named tenantId</para>
        public async Task ResetHiloSequenceFloor<T>(long floor)
        {
            foreach (var database in _store.Tenancy.BuildDatabases().OfType<IMartenDatabase>())
            {
                await database.ResetHiloSequenceFloor<T>(floor).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(string tenantId, long floor)
        {
            return _store.Tenancy.GetTenant(tenantId).Database.ResetHiloSequenceFloor<T>(floor);
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
        /// <param name="tenantId">Specify the database containing this tenant id. If omitted, this method uses the default database</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<EventStoreStatistics> FetchEventStoreStatistics(string? tenantId = null, CancellationToken token = default)
        {
            var database = tenantId == null ? _store.Tenancy.Default.Database : _store.Tenancy.GetTenant(tenantId).Database;

            var sql = $@"
select count(*) from {_store.Events.DatabaseSchemaName}.mt_events;
select count(*) from {_store.Events.DatabaseSchemaName}.mt_streams;
select last_value from {_store.Events.DatabaseSchemaName}.mt_events_sequence;
";


            await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var statistics = new EventStoreStatistics();

            using var conn = database.CreateConnection();

            await conn.OpenAsync(token).ConfigureAwait(false);

            using var reader = await conn.CreateCommand(sql).ExecuteReaderAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.EventCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.StreamCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.EventSequenceNumber = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            return statistics;
        }

        /// <summary>
        /// Check the current progress of all asynchronous projections
        /// </summary>
        /// <param name="token"></param>
        /// <param name="tenantId">Specify the database containing this tenant id. If omitted, this method uses the default database</param>
        /// <returns></returns>
        public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(string? tenantId = null, CancellationToken token = default)
        {
            var database = tenantId == null ? _store.Tenancy.Default.Database : _store.Tenancy.GetTenant(tenantId).Database;
            await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(new ProjectionProgressStatement(_store.Events),
                new ShardStateSelector());

            var session = (QuerySession)_store.QuerySession();
            await using var _ = session.ConfigureAwait(false);
            return await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Check the current progress of a single projection or projection shard
        /// </summary>
        /// <param name="tenantId">Specify the database containing this tenant id. If omitted, this method uses the default database</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<long> ProjectionProgressFor(ShardName name, string? tenantId = null, CancellationToken token = default)
        {
            var tenant = tenantId == null ? _store.Tenancy.Default : _store.Tenancy.GetTenant(tenantId);
            var database = tenant.Database;
            await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var statement = new ProjectionProgressStatement(_store.Events)
            {
                Name = name
            };

            var handler = new OneResultHandler<ShardState>(statement,
                new ShardStateSelector(), true, false);

            var session = (QuerySession)_store.QuerySession(new SessionOptions{AllowAnyTenant = true, Tenant = tenant});
            await using var _ = session.ConfigureAwait(false);

            var progress = await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

            return progress?.Sequence ?? 0;
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
