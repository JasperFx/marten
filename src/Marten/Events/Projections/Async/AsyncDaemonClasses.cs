using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public interface IStagedEventData
    {
        Task<long> LastEventProgression(string name);

        Task RegisterProgress(string name, long lastEncountered);

        Task<IEnumerable<IEvent>> FetchPage(string name, long lastEncountered, int pageSize, string[] eventTypeNames);
    }

    public class StagedEventData : IStagedEventData, IDisposable
    {
        private readonly EventGraph _events;
        private readonly ISerializer _serializer;

        public StagedEventData(IConnectionFactory factory, EventGraph events, ISerializer serializer)
        {
            _events = events;
            _serializer = serializer;
            _conn = factory.Create();

            _conn.Open();

            _sproc = new FunctionName(events.DatabaseSchemaName, "mt_mark_event_progression");
        }

        public readonly CancellationToken Cancellation = new CancellationToken();
        private readonly NpgsqlConnection _conn;
        private readonly FunctionName _sproc;

        public async Task<long> LastEventProgression(string name)
        {
            var sql = $"select last_seq_id from {_events.DatabaseSchemaName}.mt_event_progression where name = :name";
            var cmd = _conn.CreateCommand().Sql(sql).With("name", name);
            using (var reader = await cmd.ExecuteReaderAsync(Cancellation).ConfigureAwait(false))
            {
                var hasAny = await reader.ReadAsync(Cancellation).ConfigureAwait(false);

                if (!hasAny) return 0;

                return await reader.GetFieldValueAsync<long>(0, Cancellation).ConfigureAwait(false);
            }
        }

        public async Task RegisterProgress(string name, long lastEncountered)
        {
            var cmd = _conn.CreateCommand()
                .CallsSproc(_sproc)
                .With("name", name, NpgsqlDbType.Varchar)
                .With("last_encountered", lastEncountered);


            await cmd.ExecuteNonQueryAsync(Cancellation).ConfigureAwait(false);
        }

        public Task<IEnumerable<IEvent>> FetchPage(string name, long lastEncountered, int pageSize, string[] eventTypeNames)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }
    }
}