using System.Data.Common;
using System.IO;
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

        Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token);
    }

    public interface IMaybeStatefulHandler : IQueryHandler
    {
        bool DependsOnDocumentSelector();
        IQueryHandler CloneForSession(IMartenSession session, QueryStatistics statistics);

        Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token);
    }
}
