using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Baseline;
using LamarCodeGeneration;
using Marten.Linq;
using Marten.V4Internals.Linq.QueryHandlers;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.V4Internals.Linq
{
    public class LinqHandlerBuilder
    {
        private readonly IMartenSession _session;

        public LinqHandlerBuilder(IMartenSession session, Expression expression)
        {
            _session = session;
            Model = MartenQueryParser.Flyweight.GetParsedQuery(expression);
            var storage = session.StorageFor(Model.SourceType());
            TopStatement = CurrentStatement = new DocumentStatement(storage);

            // Important to deal with the selector first before you go into
            // the result operators
            switch (Model.SelectClause.Selector.NodeType)
            {
                case ExpressionType.MemberAccess:
                    CurrentStatement.ToScalar(Model.SelectClause.Selector);
                    break;

                case ExpressionType.New:
                    CurrentStatement.ToSelectTransform(Model.SelectClause);
                    break;
            }

            for (var i = 0; i < Model.BodyClauses.Count; i++)
            {
                var clause = Model.BodyClauses[i];
                switch (clause)
                {
                    case WhereClause where:
                        CurrentStatement.WhereClauses.Add(where);
                        break;
                    case OrderByClause orderBy:
                        CurrentStatement.Orderings.AddRange(orderBy.Orderings);
                        break;
                    case AdditionalFromClause additional:
                        var isComplex = Model.BodyClauses.Count > i + 1;
                        var elementType = additional.ItemType;

                        var collectionField = storage.Fields.FieldFor(additional.FromExpression);

                        var childFields = _session.Options.Storage.MappingFor(elementType);
                        CurrentStatement = CurrentStatement.ToSelectMany(collectionField, childFields, isComplex);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }

            foreach (var resultOperator in Model.ResultOperators) AddResultOperator(resultOperator);
        }

        public Statement CurrentStatement { get; }

        public Statement TopStatement { get; }


        public QueryModel Model { get; }

        public void AddResultOperator(ResultOperatorBase resultOperator)
        {
            switch (resultOperator)
            {
                case TakeResultOperator take:
                    TopStatement.Limit = (int)take.Count.Value();
                    break;

                case SkipResultOperator skip:
                    TopStatement.Offset = (int)skip.Count.Value();
                    break;

                case AnyResultOperator _:
                    CurrentStatement.ToAny();
                    break;

                case CountResultOperator _:
                    CurrentStatement.ToCount<int>();
                    break;

                case LongCountResultOperator _:
                    CurrentStatement.ToCount<long>();
                    break;

                case FirstResultOperator first:
                    CurrentStatement.Limit = 1;
                    CurrentStatement.SingleValue = true;
                    CurrentStatement.ReturnDefaultWhenEmpty = first.ReturnDefaultWhenEmpty;
                    CurrentStatement.CanBeMultiples = true;
                    break;

                case SingleResultOperator single:
                    CurrentStatement.Limit = 2;
                    CurrentStatement.SingleValue = true;
                    CurrentStatement.ReturnDefaultWhenEmpty = single.ReturnDefaultWhenEmpty;
                    CurrentStatement.CanBeMultiples = false;
                    break;

                case DistinctResultOperator _:
                    CurrentStatement.ApplySqlOperator("distinct");
                    break;

                case AverageResultOperator _:
                    CurrentStatement.ApplyAggregateOperator("AVG");
                    break;

                case SumResultOperator _:
                    CurrentStatement.ApplyAggregateOperator("SUM");
                    break;

                case MinResultOperator _:
                    CurrentStatement.ApplyAggregateOperator("MIN");
                    break;

                case MaxResultOperator _:
                    CurrentStatement.ApplyAggregateOperator("MAX");
                    break;

                default:
                    throw new NotSupportedException("Don't yet know how to deal with " + resultOperator);
            }
        }

        public IQueryHandler<TResult> BuildHandler<TResult>()
        {
            // TODO -- expression parser should be a singleton somehow to avoid
            // the object allocations
            TopStatement.CompileStructure(new MartenExpressionParser(_session.Serializer, _session.Options));

            if (CurrentStatement.SingleValue)
                return CurrentStatement.BuildSingleResultHandler<TResult>(_session);
            return CurrentStatement.SelectClause.BuildHandler<TResult>(_session, TopStatement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IQueryHandler<TResult> BuildHandler<TDocument, TResult>(ISelector<TDocument> selector,
            Statement statement)
        {
            if (typeof(TResult).CanBeCastTo<IEnumerable<TDocument>>())
            {
                return (IQueryHandler<TResult>)new ListQueryHandler<TDocument>(statement, selector);
            }

            throw new NotSupportedException("Marten does not know how to use result type " + typeof(TResult).FullNameInCode());
        }
    }
}
