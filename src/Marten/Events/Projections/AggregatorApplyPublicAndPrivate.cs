using System.Linq;
using System.Reflection;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Customize behaviour of <see cref="Aggregator{T}" /> by using private Apply methods in aggregation.
    /// </summary>
    public class AggregatorApplyPublicAndPrivate<T>: Aggregator<T> where T : class
    {
        public AggregatorApplyPublicAndPrivate() : base(typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(x => x.Name == ApplyMethod && x.GetParameters().Length == 1))
        {
        }
    }
}
