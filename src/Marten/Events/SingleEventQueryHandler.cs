using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Linq;
using Marten.Linq;
using Marten.Services;
using Marten.Util;

namespace Marten.Events
{
    internal class SingleEventQueryHandler: IQueryHandler<IEvent>
    {
        private readonly Guid _id;
        private readonly EventSelector _selector;

        public SingleEventQueryHandler(Guid id, EventGraph events, ISerializer serializer)
        {
            _id = id;
            _selector = new EventSelector(events, serializer);
        }

        public void ConfigureCommand(CommandBuilder sql, IMartenSession session)
        {
            _selector.WriteSelectClause(sql);

            var param = sql.AddParameter(_id);
            sql.Append(" where id = :");
            sql.Append(param.ParameterName);
        }

        public IEvent Handle(DbDataReader reader, IMartenSession session)
        {
            return reader.Read() ? _selector.Resolve(reader) : null;
        }

        public async Task<IEvent> HandleAsync(DbDataReader reader, IMartenSession session,
            CancellationToken token)
        {
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? await _selector.ResolveAsync(reader, token).ConfigureAwait(false)
                : null;
        }
    }
}
