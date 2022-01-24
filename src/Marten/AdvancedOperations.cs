using System;
using System.Collections.Generic;
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
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(long floor)
        {
            // TODO -- this is mildly awful, and won't work with multiple databases!
            return _store.Tenancy.Default.Database.ResetHiloSequenceFloor<T>(floor);
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
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<EventStoreStatistics> FetchEventStoreStatistics(CancellationToken token = default)
        {
            var sql = $@"
select count(*) from {_store.Events.DatabaseSchemaName}.mt_events;
select count(*) from {_store.Events.DatabaseSchemaName}.mt_streams;
select last_value from {_store.Events.DatabaseSchemaName}.mt_events_sequence;
";

            await _store.Tenancy.Default.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var statistics = new EventStoreStatistics();

            using var conn = _store.Tenancy.Default.Database.CreateConnection();

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
        /// <returns></returns>
        public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(CancellationToken token = default)
        {
            await _store.Tenancy.Default.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(new ProjectionProgressStatement(_store.Events),
                new ShardStateSelector());

            var session = (QuerySession)_store.QuerySession();
            await using var _ = session.ConfigureAwait(false);
            return await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Check the current progress of a single projection or projection shard
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<long> ProjectionProgressFor(ShardName name, CancellationToken token = default)
        {
            await _store.Tenancy.Default.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var statement = new ProjectionProgressStatement(_store.Events)
            {
                Name = name
            };

            var handler = new OneResultHandler<ShardState>(statement,
                new ShardStateSelector(), true, false);

            var session = (QuerySession)_store.QuerySession();
            await using var _ = session.ConfigureAwait(false);

            var progress = await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

            return progress?.Sequence ?? 0;
        }


        /// <summary>
        /// Calculate the source code that would be generated to handle
        /// a compiled query class
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public string SourceCodeForCompiledQuery(Type type)
        {
            if (!type.Closes(typeof(ICompiledQuery<,>)))
            {
                throw new ArgumentOutOfRangeException(nameof(type), "Not a compiled query type");
            }

            var assembly = new GeneratedAssembly(new GenerationRules(SchemaConstants.MartenGeneratedNamespace));
            using var session = _store.QuerySession();
            var plan = QueryCompiler.BuildPlan((QuerySession)session, type, _store.Options);
            var builder = new CompiledQuerySourceBuilder(plan, _store.Options);
            var (sourceType, handlerType) = builder.AssembleTypes(assembly);

            return assembly.GenerateCode();

        }

        /// <summary>
        /// Access the generated source code Marten is using for a given
        /// document type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public string SourceCodeForDocumentType(Type type)
        {
            throw new NotImplementedException("Broken, redo");
        }

        /// <summary>
        /// See the code that Marten generates for the current configuration of the
        /// Event Store
        /// </summary>
        /// <returns></returns>
        public string SourceCodeForEventStore()
        {
            throw new NotImplementedException("Broken, redo");
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
