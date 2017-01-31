using System;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class SingleEventQueryHandler : IQueryHandler<IEvent>
    {
        private readonly Guid _id;
        private readonly EventSelector _selector;

        public SingleEventQueryHandler(Guid id, EventGraph events, ISerializer serializer)
        {
            _id = id;
            _selector = new EventSelector(events, serializer);
        }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            // TODO -- use the pool. This isn't worth using StringBuilder *now*,
            // but will be when we use StringBuilder's inside of ToSelectClause()
            var sql = new StringBuilder();
            _selector.WriteSelectClause(sql, null);
            
            var param = command.AddParameter(_id);
            sql.Append(" where id = :");
            sql.Append(param.ParameterName);

            command.AppendQuery(sql.ToString());
        }

        public Type SourceType => typeof(IEvent);

        public IEvent Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.Read() ? _selector.Resolve(reader, map, stats) : null;
        }

        public async Task<IEvent> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats,
            CancellationToken token)
        {
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? await _selector.ResolveAsync(reader, map, stats, token).ConfigureAwait(false)
                : null;
        }
    }
}