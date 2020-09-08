using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using NpgsqlTypes;

namespace Marten.Linq.Fields
{
    public class EnumAsIntegerField : FieldBase
    {
        public EnumAsIntegerField(string dataLocator, Casing casing, MemberInfo[] members) : base(dataLocator, "integer", casing, members)
        {
            PgType = "integer";
            TypedLocator = $"CAST({dataLocator} ->> '{lastMemberName}' as {PgType})";
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- remove the replace
            return $"CAST({RawLocator.Replace("d.", "")} as {PgType})";
        }

        public override ISqlFragment CreateComparison(string op, ConstantExpression value)
        {
            var integer = (int)value.Value;
            return new ComparisonFilter(this, new CommandParameter(integer, NpgsqlDbType.Integer), op);
        }
    }
}
