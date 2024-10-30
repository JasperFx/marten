#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying;

internal interface IBatchQueryItem
{
    IQueryHandler Handler { get; }

    Task ReadAsync(DbDataReader reader, IMartenSession session, CancellationToken token);

}
