using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class AppendEventsOperation: IStorageOperation, IExceptionTransform
    {
        private readonly EventGraph _eventGraph;

        public AppendEventsOperation(EventStream stream, EventGraph eventGraph)
        {
            Stream = stream;
            _eventGraph = eventGraph;
        }

        public EventStream Stream { get; }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters($"select {_eventGraph.DatabaseSchemaName}.mt_append_event(?, ?, ?, ?, ?, ?, ?)");
            if (_eventGraph.StreamIdentity == StreamIdentity.AsGuid)
            {
                parameters[0].Value = Stream.Id;
                parameters[0].NpgsqlDbType = NpgsqlDbType.Uuid;
            }
            else
            {
                parameters[0].Value = Stream.Key;
                parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar;
            }

            if (Stream.AggregateType == null)
            {
                parameters[1].Value = DBNull.Value;
            }
            else
            {
                parameters[1].Value = _eventGraph.AggregateAliasFor(Stream.AggregateType);
            }
            parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;

            parameters[2].Value = session.Tenant.TenantId;
            parameters[2].NpgsqlDbType = NpgsqlDbType.Varchar;

            var events = Stream.Events.ToArray();

            // TODO -- there's an opportunity to collapse that down to just one dictionary lookup
            var eventTypes = events.Select(x => _eventGraph.EventMappingFor(x.Data.GetType()).EventTypeName).ToArray();
            var dotnetTypes = events.Select(x => _eventGraph.DotnetTypeNameFor(x.Data.GetType())).ToArray();

            var ids = events.Select(x => x.Id).ToArray();

            parameters[3].Value = ids;
            parameters[3].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;

            parameters[4].Value = eventTypes;
            parameters[4].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

            parameters[5].Value = dotnetTypes;
            parameters[5].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

            parameters[6].Value = events.Select(x => session.Serializer.ToJson(x.Data)).ToArray();
            parameters[6].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
        }

        public Type DocumentType => typeof(EventStream);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            reader.Read();
            var values = reader.GetFieldValue<int[]>(0);

            applyDataFromSproc(values);
        }

        private void applyDataFromSproc(int[] values)
        {
            Stream.ApplyLatestVersion(values[0]);

            for (int i = 1; i < values.Length; i++)
            {
                Stream.Events.ElementAt(i - 1).Sequence = values[i];
            }
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            await reader.ReadAsync(token).ConfigureAwait(false);

            var values = await reader.GetFieldValueAsync<int[]>(0, token).ConfigureAwait(false);

            applyDataFromSproc(values);
        }

        public OperationRole Role()
        {
            return OperationRole.Events;
        }

        private const string ExpectedMessage = "duplicate key value violates unique constraint \"pk_mt_events_stream_and_version\"";

        private const string StreamId = "<streamid>";
        private const string Version = "<version>";
        private static readonly Regex EventStreamUniqueExceptionDetailsRegex =
            new Regex(@"^Key \(stream_id, version\)=\((?<streamid>.*?), (?<version>\w+)\)");

        public bool TryTransform(Exception original, out Exception transformed)
        {
            if (!Matches(original))
            {
                transformed = null;
                return false;
            }

            var postgresException = (PostgresException) original.InnerException;

            object id = null;
            Type aggregateType = null;
            int expected = -1;
            int actual = -1;

            if (!string.IsNullOrEmpty(postgresException.Detail))
            {
                var details = EventStreamUniqueExceptionDetailsRegex.Match(postgresException.Detail);

                if (details.Groups[StreamId].Success)
                {
                    var streamId = details.Groups[StreamId].Value;

                    id = Guid.TryParse(streamId, out Guid guidStreamId) ? (object)guidStreamId : streamId;
                }

                if (details.Groups[Version].Success)
                {
                    var actualVersion = details.Groups[Version].Value;

                    if (int.TryParse(actualVersion, out int actualIntVersion))
                    {
                        actual = actualIntVersion;
                        expected = actual - 1;
                    }
                }
            }

            transformed = new EventStreamUnexpectedMaxEventIdException(id, aggregateType, expected, actual);
            return true;
        }

        private static bool Matches(Exception e)
        {
            return e?.InnerException is PostgresException pe
                   && pe.SqlState == PostgresErrorCodes.UniqueViolation
                   && pe.Message.Contains(ExpectedMessage);
        }
    }
}
