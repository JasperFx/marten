using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Exceptions;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Services.BatchQuerying;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class DictionaryField : FieldBase
    {
        private string _intermediateLocator;
        private bool _isStringValue;
        private string _valuePgType;
        private EnumStorage _enumStorage;

        public DictionaryField(string dataLocator, Casing casing, EnumStorage enumStorage, MemberInfo[] members) : base(dataLocator, "JSONB", casing, members)
        {
            TypedLocator = $"CAST({RawLocator} as {PgType})";
            _intermediateLocator = RawLocator.Replace("->>", "->");
            var valueType = FieldType.GenericTypeArguments[1];
            _valueIsObject = valueType == typeof(object);
            _isStringValue = valueType == typeof(string);
            _valuePgType = TypeMappings.GetPgType(valueType, enumStorage);
            _enumStorage = enumStorage;
        }

        public override string SelectorForDuplication(string pgType)
        {
            // TODO -- get rid of the replacement here
            return $"CAST({RawLocator.Replace("d.", "")} as {pgType})";
        }

        private ImHashMap<string, string> _indexLocators = ImHashMap<string, string>.Empty;
        private bool _valueIsObject;

        private string locatorForField(string key)
        {
            if (_indexLocators.TryFind(key, out var locator))
            {
                return locator;
            }

            locator = $"{RawLocator.Replace("->>", "->")} ->> '{key}'";
            if (!_valueIsObject)
            {
                locator = $"CAST({locator} as {_valuePgType})";
            }

            _indexLocators = _indexLocators.AddOrUpdate(key, locator);

            return locator;
        }

        public override ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
        {
            var key = memberExpression.As<MethodCallExpression>().Arguments[0].As<ConstantExpression>().Value;
            var locator = $"{RawLocator.Replace("->>", "->")} ->> '{key}'";


            if (value.Value == null)
            {
                return op == "=" ? (ISqlFragment) new IsNullFilter(this) : new IsNotNullFilter(this);
            }
            else
            {
                if (_valueIsObject)
                {
                    var pgType = TypeMappings.GetPgType(value.Value.GetType(), _enumStorage);
                    locator = $"CAST({locator} as {pgType})";
                }
            }

            var def = new CommandParameter(value);
            return new ComparisonFilter(new WhereFragment(locator), def, op);
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
