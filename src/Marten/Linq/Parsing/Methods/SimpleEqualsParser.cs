using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Exceptions;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods
{
    /// <summary>
    /// Implement Equals for <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="bool"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>Equals(object) calls into <see cref="Convert.ChangeType(object, Type)"/>. Equals(null) is converted to "is null" query.</remarks>
    internal class SimpleEqualsParser: IMethodCallParser
    {
        private static readonly List<Type> SupportedTypes = new List<Type> {
            typeof(int), typeof(long), typeof(decimal), typeof(Guid), typeof(bool)
        };

        private readonly string _equalsOperator;
        private readonly string _isOperator;
        private readonly bool _supportContainment;

        static SimpleEqualsParser()
        {
            SupportedTypes.AddRange(PostgresqlProvider.Instance.ResolveTypes(NpgsqlTypes.NpgsqlDbType.Timestamp));
            SupportedTypes.AddRange(PostgresqlProvider.Instance.ResolveTypes(NpgsqlTypes.NpgsqlDbType.TimestampTz));
            SupportedTypes.Add(typeof(double));
        }

        public SimpleEqualsParser(string equalsOperator = "=", string isOperator = "is", bool supportContainment = true)
        {
            _equalsOperator = equalsOperator;
            _isOperator = isOperator;
            _supportContainment = supportContainment;
        }

        public bool Matches(MethodCallExpression expression)
        {
            return SupportedTypes.Contains(expression.Method.DeclaringType) &&
                   expression.Method.Name.Equals("Equals", StringComparison.Ordinal);
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var field = GetField(mapping, expression);
            var locator = field.TypedLocator;

            ConstantExpression value;
            if (expression.Object?.NodeType == ExpressionType.Constant)
            {
                value = (ConstantExpression) expression.Object;
            }
            else
            {
                value =  expression.Arguments.OfType<ConstantExpression>().FirstOrDefault();
            }
            if (value == null)
                throw new BadLinqExpressionException("Could not extract value from {0}.".ToFormat(expression), null);

            object valueToQuery = value.Value;

            if (valueToQuery == null)
            {
                return new WhereFragment($"({field.RawLocator}) {_isOperator} null");
            }

            if (valueToQuery.GetType() != expression.Method.DeclaringType)
            {
                try
                {
                    valueToQuery = Convert.ChangeType(value.Value, expression.Method.DeclaringType);
                }
                catch (Exception e)
                {
                    throw new BadLinqExpressionException(
                        $"Could not convert {value.Value.GetType().FullName} to {expression.Method.DeclaringType}", e);
                }
            }

            if (_supportContainment && ((mapping.PropertySearching == PropertySearching.ContainmentOperator ||
                                         field.ShouldUseContainmentOperator())))
            {
                var dict = new Dictionary<string, object>();
                ContainmentWhereFragment.CreateDictionaryForSearch(dict, expression, valueToQuery, serializer);
                return new ContainmentWhereFragment(serializer, dict);
            }

            return new WhereFragment($"{locator} {_equalsOperator} ?", valueToQuery);
        }

        private static IField GetField(IFieldMapping mapping, MethodCallExpression expression)
        {
            IField GetField(Expression e)
            {
                var visitor = new FindMembers();
                visitor.Visit(e);

                var field = mapping.FieldFor(visitor.Members);
                return field;
            }

            if (!expression.Method.IsStatic && expression.Object != null && expression.Object.NodeType != ExpressionType.Constant)
            {
                // x.member.Equals(...)
                return GetField(expression.Object);
            }
            if (expression.Arguments[0].NodeType == ExpressionType.Constant)
            {
                // type.Equals("value", x.member) [decimal]
                return GetField(expression.Arguments[1]);
            }
            // type.Equals(x.member, "value") [decimal]
            return GetField(expression.Arguments[0]);
        }
    }
}
