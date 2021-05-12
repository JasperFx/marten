using System.Linq.Expressions;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.Filters
{
    // TODO -- move to Weasel
    public class WhereInArray: ISqlFragment
    {
        private readonly string _locator;
        private readonly CommandParameter _values;

        public WhereInArray(string locator, ConstantExpression values)
        {
            _locator = locator;
            _values = new CommandParameter(values);
        }

        public void Apply(CommandBuilder builder)
        {
            builder.Append(_locator);
            builder.Append(" = ANY(:");
            var parameter = _values.AddParameter(builder);
            builder.Append(parameter.ParameterName);
            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return _locator.Contains(sqlText);
        }
    }
}
