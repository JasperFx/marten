namespace Marten.Services
{
    public enum CommandRunnerMode
    {
        Transactional,
        AutoCommit,
        ReadOnly
    }
}