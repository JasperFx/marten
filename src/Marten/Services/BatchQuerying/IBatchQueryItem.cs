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

    /// <summary>
    ///     marten#6133: finish an item whose handler needs commands of its own once the batch's reader is closed
    ///     (<see cref="IBatchFollowUpHandler{T}" />). Does nothing for any other item.
    /// </summary>
    Task CompleteAsync(CancellationToken token);

    /// <summary>
    ///     Finish this item with the batch's failure, unless it already has a result.
    /// </summary>
    void Fail(System.Exception exception, CancellationToken token);
}
