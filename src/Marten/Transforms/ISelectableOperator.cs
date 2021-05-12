using Marten.Internal;
using Marten.Linq.SqlGeneration;

namespace Marten.Transforms
{
    internal interface ISelectableOperator
    {
        SelectorStatement ModifyStatement(SelectorStatement statement, IMartenSession session);
    }
}
