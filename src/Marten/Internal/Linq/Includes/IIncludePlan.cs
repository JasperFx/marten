namespace Marten.Internal.Linq.Includes
{
    public interface IIncludePlan
    {
        IIncludeReader BuildReader(IMartenSession session);

        // TODO -- something to break up the Statements
        string IdAlias { get; }
        string TempSelector { get; }
        Statement BuildStatement(string tempTableName);
    }
}
