using Marten.Internal.Operations;

namespace Marten.Linq.SqlGeneration
{
    /// <summary>
    /// Internal marker interface for organizing operations
    /// </summary>
    public interface IOperationFragment: ISqlFragment
    {
        OperationRole Role();
    }
}
