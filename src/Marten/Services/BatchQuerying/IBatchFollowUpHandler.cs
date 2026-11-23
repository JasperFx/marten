#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Services.BatchQuerying;

/// <summary>
///     marten#6133: a batch query handler whose result needs commands of its own after it reads its result set. Those
///     commands can't run while the batch's reader is open: inside a transaction the session has one connection, and
///     Npgsql refuses a second command on it. The batch reads every item first, closes its reader, then runs the
///     follow-ups in item order.
/// </summary>
/// <remarks>
///     Its only user was the batched natural-key fetch, which resolved the key in the batch and then loaded the stream
///     it found. Since #6150 a fetch by natural key resolves the key inside each of its own statements, so it needs no
///     follow-up, and nothing implements this interface.
/// </remarks>
internal interface IBatchFollowUpHandler<T>
{
    /// <summary>
    ///     Read this item's result set, and return the work that finishes the item once the batch's reader is closed.
    /// </summary>
    Task<Func<CancellationToken, Task<T>>> ReadForFollowUpAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token);
}
