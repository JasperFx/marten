using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.LastModified;
using Marten.Linq.MatchesSql;
using Marten.Linq.Parsing;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
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
            if (expression is LambdaExpression l)
            {
                expression = l.Body;
            }

            var visitor = new WhereClauseVisitor(this, mapping);
            visitor.Visit(expression);
            var whereFragment = visitor.ToWhereFragment();

            if (whereFragment == null)
            {
                throw new NotSupportedException($"Marten does not (yet) support this Linq query type ({expression})");
            }

            return whereFragment;
        }

        internal IMethodCallParser FindMethodParser(MethodCallExpression expression)
        {
            return _options.Linq.MethodCallParsers.FirstOrDefault(x => x.Matches(expression))
                         ?? _parsers.FirstOrDefault(x => x.Matches(expression));

        }

        // The out of the box method call parsers
        private static readonly IList<IMethodCallParser> _parsers = new List<IMethodCallParser>
        {
            new StringContains(),
            new EnumerableContains(),
            new StringEndsWith(),
            new StringStartsWith(),
            new StringEquals(),
            new SimpleEqualsParser(),

            // Added
            new IsOneOf(),
            new EqualsIgnoreCaseParser(),
            new IsInGenericEnumerable(),
            new IsEmpty(),
            new IsSupersetOf(),
            new IsSubsetOf(),

            // multi-tenancy
            new AnyTenant(),
            new TenantIsOneOf(),

            // soft deletes
            new MaybeDeletedParser(),
            new IsDeletedParser(),
            new DeletedSinceParser(),
            new DeletedBeforeParser(),

            // last modified
            new ModifiedSinceParser(),
            new ModifiedBeforeParser(),

            // matches sql
            new MatchesSqlParser(),

            // dictionaries
            new DictionaryExpressions(),

            // full text search
            new Search(),
            new PhraseSearch(),
            new PlainTextSearch(),
            new WebStyleSearch()
        };

        private static readonly object[] _supplementalParsers = new[]
        {
            new SimpleBinaryComparisonExpressionParser(),
        };

        private IWhereFragment buildSimpleWhereClause(IQueryableDocument mapping, BinaryExpression binary)
        {
            var isValueExpressionOnRight = binary.Right.IsValueExpression();

            var isSubQuery = isValueExpressionOnRight
                ? binary.Left is SubQueryExpression
                : binary.Right is SubQueryExpression;

            if (isSubQuery)
            {
                var jsonLocatorExpression = isValueExpressionOnRight ? binary.Left : binary.Right;
                var valueExpression = isValueExpressionOnRight ? binary.Right : binary.Left;

                var op = _operators[binary.NodeType];

                return buildChildCollectionQuery(mapping, jsonLocatorExpression.As<SubQueryExpression>().QueryModel, valueExpression, op);
            }

            var parser = _supplementalParsers.OfType<IExpressionParser<BinaryExpression>>()?.FirstOrDefault(x => x.Matches(binary));

            if (parser != null)
            {
                var where = parser.Parse(mapping, _serializer, binary);

                return where;
            }

            throw new NotSupportedException("Marten does not yet support this type of Linq query");
        }

        private IWhereFragment buildChildCollectionQuery(IQueryableDocument mapping, QueryModel query, Expression valueExpression, string op)
        {
            var field = mapping.FieldFor(query.MainFromClause.FromExpression);

            if (query.HasOperator<CountResultOperator>())
            {
                var value = field.GetValueForCompiledQueryParameter(valueExpression);

                return new WhereFragment($"jsonb_array_length({field.JSONBLocator}) {op} ?", value);
            }

            throw new NotSupportedException("Marten does not yet support this type of Linq query against child collections");
        }


    }
}
