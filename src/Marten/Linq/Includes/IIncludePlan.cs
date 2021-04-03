using Marten.Internal;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Includes
{
    public interface IIncludePlan
    {
        IIncludeReader BuildReader(IMartenSession session);

        string IdAlias { get; }
        string TempTableSelector { get; }
        bool RequiresLateralJoin();
        int Index { set; }
        string LeftJoinExpression { get; }
        string ExpressionName { get; }
        Statement BuildStatement(string tempTableName);
    }
}
