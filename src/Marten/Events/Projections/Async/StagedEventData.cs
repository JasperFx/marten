using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public class StagedEventData : IStagedEventData
    {
        private readonly NpgsqlConnection _conn;
        private readonly EventGraph _events;
        private readonly NulloIdentityMap _map;
        private readonly EventSelector _selector;

        public readonly CancellationToken Cancellation = new CancellationToken();

        public StagedEventData(DaemonOptions options, IConnectionFactory factory, EventGraph events,
            ISerializer serializer)
        {
            Options = options;
            _events = events;
            _conn = factory.Create();

            _conn.Open();

            _selector = new EventSelector(events, serializer);
            _map = new NulloIdentityMap(serializer);
        }

        public DaemonOptions Options { get; }


        public string[] EventTypeNames { get; set; } = new string[0];

        public long LastEncountered { get; set; } = -1;


        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }


        public async Task<EventPage> FetchNextPage(long lastEncountered)
        {
            var lastPossible = lastEncountered + Options.PageSize;
            var sql =
                $@"
select max(seq_id) from mt_events where seq_id > :last and seq_id <= :limit;
{_selector
                    .ToSelectClause(null)} where seq_id > :last and seq_id <= :limit and type = ANY(:types) order by seq_id;       
";

            var cmd = _conn.CreateCommand(sql)
                .With("last", lastEncountered)
                .With("limit", lastPossible)
                .With("types", Options.EventTypeNames, NpgsqlDbType.Array | NpgsqlDbType.Varchar);


            long furthestExtant;
            IList<IEvent> events = null;

            using (var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(false))
            {
                await reader.ReadAsync(Cancellation).ConfigureAwait(false);

                furthestExtant = await reader.IsDBNullAsync(0, Cancellation).ConfigureAwait(false)
                    ? 0
                    : await reader.GetFieldValueAsync<long>(0, Cancellation).ConfigureAwait(false);

                await reader.NextResultAsync(Cancellation).ConfigureAwait(false);

                events = await _selector.ReadAsync(reader, _map, Cancellation).ConfigureAwait(false);
            }


            var streams =
                events.GroupBy(x => x.StreamId)
                    .Select(
                        group => { return new EventStream(group.Key, group.OrderBy(x => x.Version).ToArray(), false); })
                    .ToArray();

            return new EventPage(lastEncountered, furthestExtant, streams) {Count = events.Count};
        }
    }
}