using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Filters
{
    public interface IReversibleWhereFragment: ISqlFragment
    {
        /// <summary>
        /// Effectively create a "reversed" NOT where fragment
        /// </summary>
        /// <returns></returns>
        ISqlFragment Reverse();
    }
}
