using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.V4Concept
{

    public abstract class StreamStateQueryHandler : IQueryHandler<StreamState>
    {
        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public StreamState Handle(DbDataReader reader, IMartenSession session)
        {
            return reader.Read() ? Resolve(session, reader) : null;
        }

        public abstract StreamState Resolve(IMartenSession session, DbDataReader reader);

        public async Task<StreamState> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            return (await reader.ReadAsync(token)) ? await ResolveAsync(session, reader, token) : null;
        }

        public abstract Task<StreamState> ResolveAsync(IMartenSession session, DbDataReader reader, CancellationToken token);

        public void SetAggregateType(StreamState state, DbDataReader reader, IMartenSession session)
        {
            var typeName = reader.IsDBNull(2) ? null : reader.GetFieldValue<string>(2);
            if (typeName.IsNotEmpty()) state.AggregateType = session.Options.Events.AggregateTypeFor(typeName);
        }

        public async Task SetAggregateTypeAsync(StreamState state, DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var typeName = await reader.IsDBNullAsync(2, token) ? null : await reader.GetFieldValueAsync<string>(2, token);
            if (typeName.IsNotEmpty()) state.AggregateType = session.Options.Events.AggregateTypeFor(typeName);
        }
    }


    internal class StreamStateByGuidSelector : ISelector<StreamState>
    {
        private readonly EventGraph _events;

        public StreamStateByGuidSelector(EventGraph events)
        {
            _events = events;
        }

        public StreamState Resolve(DbDataReader reader)
        {
            var id = reader.GetFieldValue<Guid>(0);
            var version = reader.GetFieldValue<int>(1);
            var typeName = reader.IsDBNull(2) ? null : reader.GetFieldValue<string>(2);
            var timestamp = reader.GetFieldValue<DateTime>(3);
            var created = reader.GetFieldValue<DateTime>(4);

            Type aggregateType = null;
            if (typeName.IsNotEmpty()) aggregateType = _events.AggregateTypeFor(typeName);

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }

        public async Task<StreamState> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token);
            var version = await reader.GetFieldValueAsync<int>(1, token);
            var typeName = await reader.IsDBNullAsync(2, token)
                ? null
                : await reader.GetFieldValueAsync<string>(2, token);

            var timestamp = await reader.GetFieldValueAsync<DateTime>(3, token);
            var created = await reader.GetFieldValueAsync<DateTime>(4, token);

            Type aggregateType = null;
            if (typeName.IsNotEmpty()) aggregateType = _events.AggregateTypeFor(typeName);

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }
    }

    internal class StreamStateByStringSelector : ISelector<StreamState>
    {
        private readonly EventGraph _events;

        public StreamStateByStringSelector(EventGraph events)
        {
            _events = events;
        }

        public StreamState Resolve(DbDataReader reader)
        {
            var id = reader.GetFieldValue<string>(0);
            var version = reader.GetFieldValue<int>(1);
            var typeName = reader.IsDBNull(2) ? null : reader.GetFieldValue<string>(2);
            var timestamp = reader.GetFieldValue<DateTime>(3);
            var created = reader.GetFieldValue<DateTime>(4);

            Type aggregateType = null;
            if (typeName.IsNotEmpty()) aggregateType = _events.AggregateTypeFor(typeName);

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }

        public async Task<StreamState> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<string>(0, token);
            var version = await reader.GetFieldValueAsync<int>(1, token);
            var typeName = await reader.IsDBNullAsync(2, token)
                ? null
                : await reader.GetFieldValueAsync<string>(2, token);

            var timestamp = await reader.GetFieldValueAsync<DateTime>(3, token);
            var created = await reader.GetFieldValueAsync<DateTime>(4, token);

            Type aggregateType = null;
            if (typeName.IsNotEmpty()) aggregateType = _events.AggregateTypeFor(typeName);

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }
    }
}
