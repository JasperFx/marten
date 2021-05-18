using Marten.Linq.SqlGeneration;

namespace Marten.PLv8.Transforms
{
    internal class TransformSelectClause<T> : DataSelectClause<T>
    {
        public TransformSelectClause(TransformFunction function, ISelectClause inner) : base(inner.FromObject, $"{function.Identifier}(d.data)")
        {
        }
    }
}
