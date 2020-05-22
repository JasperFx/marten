using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Fields;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class SimpleBinaryComparisonExpressionParser: IExpressionParser<BinaryExpression>
    {
        private readonly string _isOperator;
        private readonly string _wherePrefix;

        private static readonly IDictionary<ExpressionType, string> _operators = new Dictionary<ExpressionType, string>
        {
            {ExpressionType.Equal, "="},
            {ExpressionType.NotEqual, "!="},
            {ExpressionType.GreaterThan, ">"},
            {ExpressionType.GreaterThanOrEqual, ">="},
            {ExpressionType.LessThan, "<"},
            {ExpressionType.LessThanOrEqual, "<="}
        };

        public SimpleBinaryComparisonExpressionParser(string isOperator = "is", string wherePrefix = null)
        {
            _isOperator = isOperator;
            _wherePrefix = wherePrefix;
        }

        public bool Matches(BinaryExpression expression)
        {
            return expression.Type == typeof(bool) && _operators.ContainsKey(expression.NodeType);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, BinaryExpression expression)
        {
            var areBothMemberExpressions = !expression.Left.IsValueExpression() && !expression.Right.IsValueExpression();
            var isValueExpressionOnRight = areBothMemberExpressions || expression.Right.IsValueExpression();
            var jsonLocatorExpression = isValueExpressionOnRight ? expression.Left : expression.Right;
            var valueExpression = isValueExpressionOnRight ? expression.Right : expression.Left;

            var field = mapping.FieldFor(jsonLocatorExpression);

            object value;

            if (valueExpression is MemberExpression memberAccess)
            {
                var fieldOther = mapping.FieldFor(memberAccess);
                value = fieldOther.TypedLocator;
            }
            else
            {
                memberAccess = null;
                value = field.GetValueForCompiledQueryParameter(valueExpression);
            }



            var useContainment = mapping.PropertySearching == PropertySearching.ContainmentOperator || field.ShouldUseContainmentOperator();

            if (useContainment &&
                expression.NodeType == ExpressionType.Equal && value != null && !(field is DuplicatedField))
            {
                return new ContainmentWhereFragment(serializer, expression, _wherePrefix);
            }

            if (value == null)
            {
                var sql = expression.NodeType == ExpressionType.NotEqual
                    ? $"({field.RawLocator}) is not null"
                    : $"({field.RawLocator}) {_isOperator} null";

                return new WhereFragment(sql);
            }

            var jsonLocator = field.TypedLocator;

            var op = _operators[expression.NodeType];

            if (jsonLocatorExpression.NodeType == ExpressionType.Modulo)
            {
                var byValue = moduloByValue((isValueExpressionOnRight ? expression.Left : expression.Right) as BinaryExpression);
                var moduloFormat = isValueExpressionOnRight ? "{0} % {1} {2} ?" : "? {2} {0} % {1}";
                return new WhereFragment(moduloFormat.ToFormat(jsonLocator, byValue, op), value);
            }

            // ! == -> <>

            if (expression.Left.NodeType == ExpressionType.Not && expression.NodeType == ExpressionType.Equal)
            {
                op = _operators[ExpressionType.NotEqual];
            }

            // field.HasValue == true or field.HasValue == false
            if (expression.Left.NodeType == ExpressionType.NotEqual && value is bool)
            {
                jsonLocator = $"({jsonLocator}) is not null";
            }

            if (memberAccess != null)
            {
                return new WhereFragment($"{_wherePrefix}{jsonLocator} {op} {value}");
            }
            var whereFormat = isValueExpressionOnRight ? "{0} {1} ?" : "? {1} {0}";
            return new WhereFragment($"{_wherePrefix}{whereFormat.ToFormat(jsonLocator, op)}", value);

            //return value == null ? new WhereFragment($"({jsonLocator}) {_isOperator} null") : new WhereFragment($"{_wherePrefix}({jsonLocator}) {op} ?", value);
        }

        private static object moduloByValue(BinaryExpression binary)
        {
            var moduloValueExpression = binary?.Right as ConstantExpression;
            return moduloValueExpression != null ? moduloValueExpression.Value : 1;
        }
    }
}
