using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Filters
{
    public class ComparisonFilter: IReversibleWhereFragment
    {
        public ComparisonFilter(ISqlFragment left, ISqlFragment right, string op)
        {
            Left = left;
            Right = right;
            Op = op;
        }

        public ISqlFragment Left { get; }

        public ISqlFragment Right { get; }

        public string Op { get; private set; }

        public void Apply(CommandBuilder builder)
        {
            Left.Apply(builder);
            builder.Append(" ");
            builder.Append(Op);
            builder.Append(" ");
            Right.Apply(builder);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }

        public ISqlFragment Reverse()
        {
            Op = WhereClauseParser.NotOperators[Op];
            return this;
        }
    }
}
