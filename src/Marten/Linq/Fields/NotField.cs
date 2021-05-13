using System.Linq.Expressions;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Fields
{
    public class NotField: IComparableFragment
    {
        private readonly IField _inner;

        public NotField(IField inner)
        {
            _inner = inner;
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
        {
            var opposite = ComparisonFilter.NotOperators[op];
            return _inner.CreateComparison(opposite, value, memberExpression);
        }
    }
}
