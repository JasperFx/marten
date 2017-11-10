namespace Marten.Services
{
    public enum CommandRunnerMode
    {
        Transactional,
        AutoCommit,
        ReadOnly,

        /// <summary>
        /// Implies that some other process is controlling the transaction boundaries
        /// </summary>
        External
    }
}