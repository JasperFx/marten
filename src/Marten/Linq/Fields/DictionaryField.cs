using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Services.BatchQuerying;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class DictionaryField : FieldBase
    {
        private string _intermediateLocator;
        private bool _isStringValue;
        private string _valuePgType;

        public DictionaryField(string dataLocator, Casing casing, EnumStorage enumStorage, MemberInfo[] members) : base(dataLocator, "JSONB", casing, members)
        {
            TypedLocator = $"CAST({RawLocator} as {PgType})";
            _intermediateLocator = RawLocator.Replace("->>", "->");
            var valueType = FieldType.GenericTypeArguments[1];
            _isStringValue = valueType == typeof(string);
            _valuePgType = TypeMappings.GetPgType(valueType, enumStorage);
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- get rid of the replacement here
            return $"CAST({RawLocator.Replace("d.", "")} as {pgType})";
        }

        public override string ToOrderExpression(Expression expression)
        {
            if (expression is MethodCallExpression m)
            {
                var value = m.Arguments[0];
                if (value is ConstantExpression item)
                {
                    return _isStringValue
                        ? $"{_intermediateLocator} ->> '{item.Value}'"
                        : $"CAST({_intermediateLocator} ->> '{item.Value}' as {_valuePgType})";

                }
                else
                {
                    throw new BadLinqExpressionException("Marten cannot determine the ORDER BY SQL for this usage of a Dictionary");
                }
            }

            return base.ToOrderExpression(expression);
        }
    }
}
