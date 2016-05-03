using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

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
            var sql = _selector.ToSelectClause(null);

            var param = command.AddParameter(_id);
            sql += " where id = :" + param.ParameterName;

            command.AppendQuery(sql);
        }

        public Type SourceType => typeof(IEvent);
        public IEvent Handle(DbDataReader reader, IIdentityMap map)
        {
            return reader.Read() ? _selector.Resolve(reader, map) : null;
        }

        public async Task<IEvent> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.ReadAsync(token).ConfigureAwait(false) 
                ? await _selector.ResolveAsync(reader, map, token).ConfigureAwait(false) 
                : null;
        }
    }
}