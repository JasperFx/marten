using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Services;
using Marten.Util;
using Marten.V4Internals.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.V4Internals
{
    public interface IQueryHandler
    {
        void ConfigureCommand(CommandBuilder builder, IMartenSession session);
    }


    public interface IQueryHandler<T> : IQueryHandler
    {
        T Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats);

        Task<T> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token);
    }


}
