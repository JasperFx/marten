using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Querying;

internal class SingleEventQueryHandler: IQueryHandler<IEvent>
{
    private readonly Guid _id;
    private readonly IEventStorage _selector;

    public SingleEventQueryHandler(Guid id, IEventStorage selector)
    {
        _id = id;
        _selector = selector;
    }

    public void ConfigureCommand(ICommandBuilder sql, IMartenSession session)
    {
        _selector.Apply(sql);

        sql.Append(" where id = ");
        sql.AppendParameter(_id);
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

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
