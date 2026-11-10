#nullable enable

namespace Marten.Services.BatchQuerying;

/// <summary>
///     Marks a batch query handler that can read a stream's version with <c>for update</c>, so a batch that
///     times out waiting on that row lock can report it as a <see cref="JasperFx.Events.StreamLockedException" />.
/// </summary>
internal interface IExclusiveFetchHandler
{
    /// <summary>
    ///     The stream this handler locks, or null when it is not an exclusive fetch. The <c>for update</c> is
    ///     the first statement the handler writes.
    /// </summary>
    object? ExclusiveStreamId { get; }
}
