using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public class StagedEventOptions
    {
        public string Name { get; set; } = Guid.NewGuid().ToString();
        public int PageSize { get; set; } = 100;
        public string[] EventTypeNames { get; set; } = new string[0];
    }

    public interface IStagedEventData
    {

        Task<long> LastEventProgression();

        Task RegisterProgress(long lastEncountered);


        Task<IList<IEvent>> FetchNextPage();

    }

    public class StagedEventData : IStagedEventData, IDisposable
    {
        private readonly StagedEventOptions _options;
        private readonly EventGraph _events;
        private readonly ISerializer _serializer;

        public StagedEventData(StagedEventOptions options, IConnectionFactory factory, EventGraph events, ISerializer serializer)
        {
            _options = options;
            _events = events;
            _serializer = serializer;
            _conn = factory.Create();

            _conn.Open();

            _selector = new EventSelector(events, serializer);

            _sproc = new FunctionName(events.DatabaseSchemaName, "mt_mark_event_progression");
        }

        public readonly CancellationToken Cancellation = new CancellationToken();
        private readonly NpgsqlConnection _conn;
        private readonly FunctionName _sproc;
        private readonly EventSelector _selector;


        public string[] EventTypeNames { get; set; } = new string[0];

        public async Task<long> LastEventProgression()
        {
            var sql = $"select last_seq_id from {_events.DatabaseSchemaName}.mt_event_progression where name = :name";
            var cmd = _conn.CreateCommand().Sql(sql).With("name", _options.Name);
            using (var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(false))
            {
                var hasAny = await reader.ReadAsync(Cancellation).ConfigureAwait(false);

                if (!hasAny) return 0;

                return await reader.GetFieldValueAsync<long>(0, Cancellation).ConfigureAwait(false);
            }
        }

        public long LastEncountered { get; set; } = -1;

        public async Task RegisterProgress(long lastEncountered)
        {
            var cmd = _conn.CreateCommand()
                .CallsSproc(_sproc)
                .With("name", _options.Name, NpgsqlDbType.Varchar)
                .With("last_encountered", lastEncountered);


            await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(false);

            LastEncountered = lastEncountered;
        }

        public async Task<IList<IEvent>> FetchNextPage()
        {
            var cmd = await BuildPageFetchCommand();

            var events = await fetchEvents(cmd);

            return events;
        }

        private async Task<IList<IEvent>> fetchEvents(NpgsqlCommand cmd)
        {
            using (var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(false))
            {
                return await _selector.ReadAsync(reader, new NulloIdentityMap(_serializer), Cancellation).ConfigureAwait(false);
            }
        }

        private async Task<NpgsqlCommand> BuildPageFetchCommand()
        {
            var sql = _selector.ToSelectClause(null);

            if (LastEncountered < 0)
            {
                LastEncountered = await LastEventProgression().ConfigureAwait(false);
            }

            var cmd = _conn.CreateCommand();
            cmd.AddParameter("last", LastEncountered);
            cmd.AddParameter("limit", _options.PageSize);

            sql += " where seq_id > :last";
            if (_options.EventTypeNames.Any())
            {
                cmd.With("types", _options.EventTypeNames, NpgsqlDbType.Array | NpgsqlDbType.Varchar);

                sql += " and type = ANY(:types)";
            }

            sql += " LIMIT :limit";

            cmd.CommandText = sql;

            return cmd;
        }


        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }
    }


}