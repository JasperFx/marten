using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandler<T>
    {
        Type SourceType { get; }

        // It's done this way so that the same query handler can swing back
        // and forth between batched queries and standalone queries
        void ConfigureCommand(NpgsqlCommand command);

        T Handle(DbDataReader reader, IIdentityMap map);

        Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);
    }
}