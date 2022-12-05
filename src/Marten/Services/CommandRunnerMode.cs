namespace Marten.Services;

public enum CommandRunnerMode
{
    /// <summary>
    ///     Marten controls the transactional boundary
    /// </summary>
    Transactional,
    ReadOnly,

    /// <summary>
    ///     Implies that some other process is controlling the transaction boundaries
    /// </summary>
    External
}
