using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
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

            var members = FindMembers.Determine(jsonLocatorExpression);

            var field = mapping.FieldFor(members);

            object value;

            if (valueExpression is MemberExpression memberAccess)
            {
                var membersOther = FindMembers.Determine(memberAccess);
                var fieldOther = mapping.FieldFor(membersOther);
                value = fieldOther.SqlLocator;
            }
            else
            {
                memberAccess = null;
                value = field.GetValue(valueExpression);
            }

            var jsonLocator = field.SqlLocator;

            var useContainment = mapping.PropertySearching == PropertySearching.ContainmentOperator || field.ShouldUseContainmentOperator();

            var isDuplicated = field is DuplicatedField || field is IdField;
            var isEnumString = field.MemberType.GetTypeInfo().IsEnum && serializer.EnumStorage == EnumStorage.AsString;

            if (useContainment &&
                expression.NodeType == ExpressionType.Equal && value != null && !isDuplicated && !isEnumString)
            {
                return new ContainmentWhereFragment(serializer, expression, _wherePrefix);
            }

            if (value == null)
            {
                var sql = expression.NodeType == ExpressionType.NotEqual
                    ? $"({jsonLocator}) is not null"
                    : $"({jsonLocator}) {_isOperator} null";

                return new WhereFragment(sql);
            }

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
