using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Util;

namespace Marten.Events.Projections.Async
{

    public class CompleteRebuild : IDisposable, IEventPageWorker
    {
        private readonly IDocumentStore _store;
        private readonly IProjection _projection;
        private readonly Fetcher _fetcher;
        private readonly IDocumentSession _session;
        private readonly ProjectionTrack _track;
        private readonly TaskCompletionSource<long> _completion = new TaskCompletionSource<long>();
        private long _lastEncountered;

        public CompleteRebuild(StagedEventOptions options, IDocumentStore store, IProjection projection)
        {
            _store = store;
            _projection = projection;
            var storeOptions = store.Advanced.Options;
            var events = store.Schema.Events.As<EventGraph>();

            options.EventTypeNames = projection.Consumes.Select(x => events.EventMappingFor(x).Alias).ToArray();

            var eventData = new StagedEventData(options, storeOptions.ConnectionFactory(), events, storeOptions.Serializer());
            _fetcher = new Fetcher(eventData);

            // TODO -- may want to be purging the identity map as you go
            _session = store.OpenSession();
            _track = new ProjectionTrack(events, projection, _session);
        }

        public async Task<long> PerformRebuild(CancellationToken token)
        {
            _store.Schema.EnsureStorageExists(_projection.Produces);

            await clearExistingState(token).ConfigureAwait(false);

            _fetcher.Start(this, false);

            return await _completion.Task;
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
            _track.Dispose();
            _session.Dispose();
        }

        void IEventPageWorker.QueuePage(EventPage page)
        {
            Console.WriteLine("Got " + page);

            if (page.Count != 0)
            {
                _lastEncountered = page.To;
                _track.QueuePage(page);
            }
        }

        public void Finished(long lastEncountered)
        {
            _track.WaitUntilEventIsProcessed(_lastEncountered).ContinueWith(t =>
            {
                _completion.SetResult(lastEncountered);
            });
        }
    }
}