using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    internal interface ISingleQueryRunner
    {
        Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation);
        Task SingleCommit(DbCommand command, CancellationToken cancellation);
    }
}