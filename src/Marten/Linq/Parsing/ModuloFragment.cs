using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Parsing
{
    internal class ModuloFragment : IComparableFragment, ISqlFragment
    {
        private readonly ISqlFragment _left;
        private readonly ISqlFragment _right;
        private string _op;
        private CommandParameter _value;

        public ModuloFragment(BinaryExpression expression, IFieldMapping fields)
        {
            _left = analyze(expression.Left, fields);
            _right = analyze(expression.Right, fields);
        }

        private ISqlFragment analyze(Expression expression, IFieldMapping fields)
        {
            if (expression is ConstantExpression c)
            {
                return new CommandParameter(c);
            }
            else
            {
                return fields.FieldFor(expression);
            }
        }

        public ISqlFragment CreateComparison(string op, ConstantExpression value)
        {
            _op = " " + op + " ";
            _value = new CommandParameter(value);

            return this;
        }

        public void Apply(CommandBuilder builder)
        {
            _left.Apply(builder);
            builder.Append(" % ");
            _right.Apply(builder);
            builder.Append(_op);
            _value.Apply(builder);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
