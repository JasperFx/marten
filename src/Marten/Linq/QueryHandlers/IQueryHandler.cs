using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandler
    {
        void ConfigureCommand(NpgsqlCommand command);
    }

    public interface IQueryHandler<T> : IQueryHandler
    {
        Type SourceType { get; }

        T Handle(DbDataReader reader, IIdentityMap map);

        Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);
    }
}