using System.Linq.Expressions;
using Baseline;
using Baseline.Conversion;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public class SelectManyQuery
    {
        private static readonly Conversions conversions = new Conversions();

        private readonly IField _field;
        private readonly QuerySourceReferenceExpression _expression;
        private readonly Expression _from;

        public SelectManyQuery(IQueryableDocument mapping, QueryModel query)
        {
            _expression = query.SelectClause.Selector.As<QuerySourceReferenceExpression>();
            _from = _expression.ReferencedQuerySource.As<AdditionalFromClause>().FromExpression;

            var members = FindMembers.Determine(_from);
            _field = mapping.FieldFor(members);

            IsDistinct = query.HasOperator<DistinctResultOperator>();
        }

        public ISelector<T> ToSelector<T>(ISerializer serializer)
        {
            if (typeof(T) == typeof(string))
            {
                return new SingleFieldSelector<T>(IsDistinct, $"jsonb_array_elements_text({_field.SqlLocator})");
            }
            else if (TypeMappings.HasTypeMapping(typeof(T)))
            {
                return new ArrayElementFieldSelector<T>(IsDistinct, _field, conversions);
            }

            return new DeserializeSelector<T>(serializer, $"jsonb_array_elements_text({_field.SqlLocator})");

        }

        public string SqlLocator => _field.SqlLocator;

        public bool IsDistinct { get; }
    }
}