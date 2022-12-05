namespace Marten.Events.Projections;

public enum ProjectionLifecycle
{
    /// <summary>
    ///     The projection will be updated in the same transaction as
    ///     the events being captured
    /// </summary>
    Inline,

    /// <summary>
    ///     The projection will only execute within the Async Daemon
    /// </summary>
    Async,

    /// <summary>
    ///     The projection is only executed on demand
    /// </summary>
    Live
}
