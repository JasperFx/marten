using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Events.Querying
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
            if (typeName.IsNotEmpty()) state.AggregateType = session.Options.EventGraph.AggregateTypeFor(typeName);
        }

        public async Task SetAggregateTypeAsync(StreamState state, DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var typeName = await reader.IsDBNullAsync(2, token) ? null : await reader.GetFieldValueAsync<string>(2, token);
            if (typeName.IsNotEmpty()) state.AggregateType = session.Options.EventGraph.AggregateTypeFor(typeName);
        }
    }

}
