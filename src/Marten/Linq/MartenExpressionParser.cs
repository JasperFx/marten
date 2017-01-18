using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.LastModified;
using Marten.Linq.Parsing;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public partial class MartenExpressionParser
    {
        public static readonly string CONTAINS = nameof(string.Contains);
        public static readonly string STARTS_WITH = nameof(string.StartsWith);
        public static readonly string ENDS_WITH = nameof(string.EndsWith);

        private static readonly IDictionary<ExpressionType, string> _operators = new Dictionary<ExpressionType, string>
        {
            {ExpressionType.Equal, "="},
            {ExpressionType.NotEqual, "!="},
            {ExpressionType.GreaterThan, ">"},
            {ExpressionType.GreaterThanOrEqual, ">="},
            {ExpressionType.LessThan, "<"},
            {ExpressionType.LessThanOrEqual, "<="}
        };

        private readonly ISerializer _serializer;
        private readonly StoreOptions _options;

        public MartenExpressionParser(ISerializer serializer, StoreOptions options)
        {
            _serializer = serializer;
            _options = options;
        }

        public IWhereFragment ParseWhereFragment(IQueryableDocument mapping, Expression expression)
        {
            if (expression is LambdaExpression)
            {
                expression = expression.As<LambdaExpression>().Body;
            }

            var visitor = new WhereClauseVisitor(this, mapping);
            visitor.Visit(expression);
            var whereFragment = visitor.ToWhereFragment();

            if (whereFragment == null)
            {
                throw new NotSupportedException("Marten does not (yet) support this Linq query type");
            }

            return whereFragment;
        }

        // The out of the box method call parsers
        private static readonly IList<IMethodCallParser> _parsers = new List<IMethodCallParser>
        {
            new StringContains(),
            new EnumerableContains(),
            new StringEndsWith(),
            new StringStartsWith(),
            new StringEquals(),

            // Added
            new IsOneOf(),
            new IsInGenericEnumerable(),
            new IsEmpty(),

            // soft deletes
            new MaybeDeletedParser(),
            new IsDeletedParser(),

            // last modified
            new ModifiedSinceParser(),
            new ModifiedBeforeParser(),

            // dictionaries
            new DictionaryExpressions()
        };

        // TODO -- pull this out somewhere else
        private IWhereFragment buildSimpleWhereClause(IQueryableDocument mapping, BinaryExpression binary)
        {
            var isValueExpressionOnRight = binary.Right.IsValueExpression();
            var jsonLocatorExpression = isValueExpressionOnRight ? binary.Left : binary.Right;
            var valueExpression = isValueExpressionOnRight ? binary.Right : binary.Left;

            var op = _operators[binary.NodeType];

            var isSubQuery = isValueExpressionOnRight
                ? binary.Left is SubQueryExpression
                : binary.Right is SubQueryExpression;

            if (isSubQuery)
            {
                return buildChildCollectionQuery(mapping, jsonLocatorExpression.As<SubQueryExpression>().QueryModel, valueExpression, op);
            }

            var members = FindMembers.Determine(jsonLocatorExpression);

            var field = mapping.FieldFor(members);


            var value = field.GetValue(valueExpression);
            var jsonLocator = field.SqlLocator;

            var useContainment = mapping.PropertySearching == PropertySearching.ContainmentOperator || field.ShouldUseContainmentOperator();

            var isDuplicated = (mapping.FieldFor(members) is DuplicatedField);

            if (useContainment &&
                binary.NodeType == ExpressionType.Equal && value != null && !isDuplicated)
            {
                return new ContainmentWhereFragment(_serializer, binary);
            }

            

            if (value == null)
            {
                var sql = binary.NodeType == ExpressionType.NotEqual
                    ? $"({jsonLocator}) is not null"
                    : $"({jsonLocator}) is null";

                return new WhereFragment(sql);
            }
            if (jsonLocatorExpression.NodeType == ExpressionType.Modulo)
            {
                var moduloByValue = MartenExpressionParser.moduloByValue(binary);
                return new WhereFragment("{0} % {1} {2} ?".ToFormat(jsonLocator, moduloByValue, op), value);
            }


            return new WhereFragment("{0} {1} ?".ToFormat(jsonLocator, op), value);
        }

        private IWhereFragment buildChildCollectionQuery(IQueryableDocument mapping, QueryModel query, Expression valueExpression, string op)
        {
            var members = FindMembers.Determine(query.MainFromClause.FromExpression);
            var field = mapping.FieldFor(members);

            if (query.HasOperator<CountResultOperator>())
            {
                var value = field.GetValue(valueExpression);

                return new WhereFragment($"jsonb_array_length({field.SqlLocator}) {op} ?", value);
            }

            throw new NotSupportedException("Marten does not yet support this type of Linq query against child collections");
        }

        private static object moduloByValue(BinaryExpression binary)
        {
            var moduloExpression = binary.Left as BinaryExpression;
            var moduloValueExpression = moduloExpression?.Right as ConstantExpression;
            return moduloValueExpression != null ? moduloValueExpression.Value : 1;
        }
    }
}
