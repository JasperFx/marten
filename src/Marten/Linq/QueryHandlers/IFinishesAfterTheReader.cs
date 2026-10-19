#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Linq.QueryHandlers;

/// <summary>
///     A query handler whose result may need another read on the session -- the cold part of an event
///     stream -- which cannot happen while the batch's reader is still open on the connection. A batch
///     reads every item first, closes the reader, and only then finishes these.
/// </summary>
internal interface IFinishesAfterTheReader<T>: IQueryHandler<T>
{
    Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token);

    Task<T> FinishAsync(IStorageSession session, CancellationToken token);
}
