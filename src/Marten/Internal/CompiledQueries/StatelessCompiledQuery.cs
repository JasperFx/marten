using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

public abstract class StatelessCompiledQuery<TOut, TQuery>: IQueryHandler<TOut>
{
    private readonly IQueryHandler<TOut> _inner;
    protected readonly TQuery _query;

    public StatelessCompiledQuery(IQueryHandler<TOut> inner, TQuery query)
    {
        _inner = inner;
        _query = query;
    }

    public abstract void ConfigureCommand(IPostgresqlCommandBuilder builder, IMartenSession session);

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        return _inner.StreamJson(stream, reader, token);
    }

    public TOut Handle(DbDataReader reader, IMartenSession session)
    {
        return _inner.Handle(reader, session);
    }

    public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        return _inner.HandleAsync(reader, session, token);
    }

    protected string StartsWith(string value)
    {
        return $"%{value}";
    }

    protected string ContainsString(string value)
    {
        return $"%{value}%";
    }

    protected string EndsWith(string value)
    {
        return $"{value}%";
    }
}
