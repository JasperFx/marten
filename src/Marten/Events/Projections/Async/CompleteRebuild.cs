using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Util;

namespace Marten.Events.Projections.Async
{

    public class CompleteRebuild : IDisposable
    {
        private readonly IDocumentStore _store;
        private readonly IProjection _projection;
        private readonly Fetcher _fetcher;
        private readonly ProjectionTrack _projectionTrack;

        public CompleteRebuild(IDocumentStore store, IProjection projection)
        {
            _store = store;
            _projection = projection;
            _fetcher = new Fetcher(store, _projection.AsyncOptions, _projection.Consumes);

            _projectionTrack = new ProjectionTrack(_fetcher, store, projection);

        }

        public async Task<long> PerformRebuild(CancellationToken token)
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            await clearExistingState(token).ConfigureAwait(false);

            return await _projectionTrack.RunUntilEndOfEvents().ConfigureAwait(false);

        }

        private async Task clearExistingState(CancellationToken token)
        {
            var tableName = _store.Schema.MappingFor(_projection.Produces).Table;
                var sql = $"delete from {_store.Schema.Events.DatabaseSchemaName}.mt_event_progression where name = :name;truncate {tableName} cascade";

            using (var conn = _store.Advanced.OpenConnection())
            {
                await conn.ExecuteAsync(async (cmd, tkn) =>
                {
                    await cmd.Sql(sql)
                        .With("name", _projection.Produces.FullName)
                        .ExecuteNonQueryAsync(tkn)
                        .ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }

            Console.WriteLine("Cleared the Existing Projection State for " + _projection.Produces.FullName);
        }

        public void Dispose()
        {
            _fetcher.Dispose();
            _projectionTrack.Dispose();
        }



    }
}