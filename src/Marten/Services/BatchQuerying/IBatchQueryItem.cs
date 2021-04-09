using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        IQueryHandler Handler { get; }

        Task ReadAsync(DbDataReader reader, IMartenSession session, CancellationToken token);

        void Read(DbDataReader reader, IMartenSession session);
    }
}
