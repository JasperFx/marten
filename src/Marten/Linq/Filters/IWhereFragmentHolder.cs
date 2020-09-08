using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Filters
{
    public interface IWhereFragmentHolder
    {
        void Register(ISqlFragment fragment);
    }
}
