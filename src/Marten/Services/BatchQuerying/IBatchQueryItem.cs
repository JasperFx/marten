using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        IQueryHandler Handler { get; }

        QueryStatistics Stats { get; }

        Task Read(DbDataReader reader, IIdentityMap map, CancellationToken token);

        void Read(DbDataReader reader, IIdentityMap map);
    }
}