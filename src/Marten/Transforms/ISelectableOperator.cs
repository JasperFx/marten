using Marten.Internal;
using Marten.Internal.Linq;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Transforms
{
    public interface ISelectableOperator
    {
        Statement ModifyStatement(Statement statement, IMartenSession session);
    }
}
