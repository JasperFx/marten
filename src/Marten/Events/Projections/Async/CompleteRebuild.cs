using System;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Util;

namespace Marten.Events.Projections.Async
{

    public class CompleteRebuild : IEventPageWorker, IDisposable
    {
        private readonly IDocumentStore _store;
        private readonly IProjection _projection;
        private readonly IStagedEventData _eventData;
        private Fetcher _fetcher;
        private IDocumentSession _session;
        private ProjectionTrack _track;

        public CompleteRebuild(StagedEventOptions options, IDocumentStore store, IProjection projection)
        {
            _store = store;
            _projection = projection;
            var storeOptions = store.Advanced.Options;
            var events = store.Schema.Events.As<EventGraph>();

            options.EventTypeNames = projection.Consumes.Select(x => events.EventMappingFor(x).Alias).ToArray();

            _eventData = new StagedEventData(options, storeOptions.ConnectionFactory(), events, storeOptions.Serializer());
            //_fetcher = new Fetcher(_eventData, this);

            // TODO -- may want to be purging the identity map as you go
            _session = store.OpenSession();
            _track = new ProjectionTrack(events, projection, _session);
        }

        public async Task PerformRebuild(CancellationToken token)
        {
            await clearExistingState(token).ConfigureAwait(false);



            throw new NotImplementedException();
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
        }

        void IEventPageWorker.Receive(EventPage page)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}