using System.Linq.Expressions;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Fields
{
    public class NotField: IComparableFragment
    {
        private readonly IField _inner;

        public NotField(IField inner)
        {
            _inner = inner;
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value)
        {
            var opposite = WhereClauseParser.NotOperators[op];
            return _inner.CreateComparison(opposite, value);
        }
    }
}
