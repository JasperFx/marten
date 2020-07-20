using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Linq;
using Marten.Linq;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        IQueryHandler Handler { get; }

        Task ReadAsync(DbDataReader reader, IMartenSession session, CancellationToken token);

        void Read(DbDataReader reader, IMartenSession session);
    }
}
