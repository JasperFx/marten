using Marten.Internal.Operations;

namespace Marten.Linq.SqlGeneration
{
    public interface IOperationFragment: ISqlFragment
    {
        OperationRole Role();
    }
}
