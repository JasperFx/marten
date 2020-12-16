using System;
using System.Linq;
using System.Reflection;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Customize behaviour of <see cref="Aggregator{T}" /> by using private Apply methods in aggregation.
    /// </summary>
    [Obsolete("This will be eliminated in V4")]
    public class AggregatorApplyPrivate<T>: Aggregator<T> where T : class
    {
        public AggregatorApplyPrivate() : base(typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(x => x.Name == ApplyMethod && x.GetParameters().Length == 1))
        {
        }
    }
}
