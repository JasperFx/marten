using Marten.Internal;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Includes
{
    public interface IIncludePlan
    {
        IIncludeReader BuildReader(IMartenSession session);

        // TODO -- something to break up the Statements
        string IdAlias { get; }
        string TempSelector { get; }
        int Index { set; }
        Statement BuildStatement(string tempTableName);
    }
}
