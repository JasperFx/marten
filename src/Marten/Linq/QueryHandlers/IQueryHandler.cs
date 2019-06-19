using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandler
    {
        void ConfigureCommand(CommandBuilder builder);
    }

    public interface IReaderHandler<T>
    {
        T Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats);

        Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token);
    }

    public interface IQueryHandler<T>: IQueryHandler, IReaderHandler<T>
    {
        Type SourceType { get; }
    }
}
