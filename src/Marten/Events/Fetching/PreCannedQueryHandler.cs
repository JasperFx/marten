using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal class PreCannedQueryHandler<T>: IQueryHandler<T>
{
    private readonly T _value;

    public PreCannedQueryHandler(T value)
    {
        _value = value;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("select 1");
    }

    public T Handle(DbDataReader reader, IMartenSession session)
    {
        return _value;
    }

    public Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        return Task.FromResult(_value);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotImplementedException();
    }
}
