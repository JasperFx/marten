using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Filters
{
    // TODO -- move to Weasel
    public interface IWhereFragmentHolder
    {
        void Register(ISqlFragment fragment);
    }
}
