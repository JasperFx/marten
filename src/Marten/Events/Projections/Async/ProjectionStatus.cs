using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.Events.Projections.Async
{
    public class ProjectionStatus
    {
        private readonly ConcurrentDictionary<string, long> _progress = new ConcurrentDictionary<string, long>();


        public async Task Initialize(IDocumentStore store, CancellationToken token)
        {
            var eventSchema = store.Advanced.Options.Events.DatabaseSchemaName;
            var sql = $"select name, last_seq_id from {eventSchema}.mt_event_progression";

            using (var conn = store.Advanced.OpenConnection())
            {
                await conn.ExecuteAsync(async (cmd, tkn) =>
                {
                    using (var reader = await cmd.Sql(sql).ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            var name = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                            var last = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);

                            _progress[name] = last;
                        }
                    }
                }, token).ConfigureAwait(false);
            }
        }

        public void UpdateLastEncountered(string viewType, long lastEncountered)
        {
            _progress[viewType] = lastEncountered;
        }

        public long LastEncountered(string viewType)
        {
            return _progress.GetOrAdd(viewType, key => 0);
        }

        public long FarthestBehind()
        {
            return _progress.Values.Min();
        }

        public KeyValuePair<string, long>[] Pairs()
        {
            return _progress.ToArray();
        }
    }
}