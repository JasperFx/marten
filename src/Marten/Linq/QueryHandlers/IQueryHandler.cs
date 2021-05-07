using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Weasel.Postgresql;
using Marten.Util;
#nullable enable
namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandler
    {
        void ConfigureCommand(CommandBuilder builder, IMartenSession session);
    }


    public interface IQueryHandler<T>: IQueryHandler
    {
        T Handle(DbDataReader reader, IMartenSession session);

        Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token);
    }

    public interface IMaybeStatefulHandler
    {
        bool DependsOnDocumentSelector();
        IQueryHandler CloneForSession(IMartenSession session, QueryStatistics statistics);
    }
}
