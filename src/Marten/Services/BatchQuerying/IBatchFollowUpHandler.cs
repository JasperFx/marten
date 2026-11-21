#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Services.BatchQuerying;

/// <summary>
///     marten#6133: a batch query handler whose result needs commands of its own after it reads its result set, such
///     as a natural-key fetch that resolves the key in the batch and then loads the stream it found. Those commands
///     can't run while the batch's reader is open: inside a transaction the session has one connection, and Npgsql
///     refuses a second command on it. The batch reads every item first, closes its reader, then runs the follow-ups
///     in item order.
/// </summary>
internal interface IBatchFollowUpHandler<T>
{
    /// <summary>
    ///     Read this item's result set, and return the work that finishes the item once the batch's reader is closed.
    /// </summary>
    Task<Func<CancellationToken, Task<T>>> ReadForFollowUpAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token);
}
