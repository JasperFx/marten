using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal.Linq.Includes;
using Marten.Internal.Linq.QueryHandlers;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Transforms;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Internal.Linq
{
    public partial class LinqHandlerBuilder
    {
        private readonly IMartenSession _session;

        private static IList<IMethodCallMatcher> _methodMatchers = new List<IMethodCallMatcher>
        {
            new AsJsonMatcher(),
            new TransformToJsonMatcher(),
            new TransformToOtherMatcher()
        };

        public LinqHandlerBuilder(IMartenSession session, Expression expression, ResultOperatorBase additionalOperator = null, bool forCompiled = false)
        {
            _session = session;
            Model = forCompiled
                ? MartenQueryParser.TransformQueryFlyweight.GetParsedQuery(expression)
                : MartenQueryParser.Flyweight.GetParsedQuery(expression);

            if (additionalOperator != null) Model.ResultOperators.Add(additionalOperator);

            var storage = session.StorageFor(Model.SourceType());
            TopStatement = CurrentStatement = new DocumentStatement(storage);


            // TODO -- this probably needs to get fancier later
            if (Model.MainFromClause.FromExpression is SubQueryExpression sub)
            {
                processQueryModel(sub.QueryModel, storage, true);
                processQueryModel(Model, storage, false);
            }
            else
            {
                processQueryModel(Model, storage, true);
            }


        }

        private void processQueryModel(QueryModel queryModel, IDocumentStorage storage, bool considerSelectors)
        {
            for (var i = 0; i < queryModel.BodyClauses.Count; i++)
            {
                var clause = queryModel.BodyClauses[i];
                switch (clause)
                {
                    case WhereClause where:
                        CurrentStatement.WhereClauses.Add(@where);
                        break;
                    case OrderByClause orderBy:
                        CurrentStatement.Orderings.AddRange(orderBy.Orderings);
                        break;
                    case AdditionalFromClause additional:
                        var isComplex = queryModel.BodyClauses.Count > i + 1 || queryModel.ResultOperators.Any();
                        var elementType = additional.ItemType;
                        var collectionField = storage.Fields.FieldFor(additional.FromExpression);

                        CurrentStatement = CurrentStatement.ToSelectMany(collectionField, _session, isComplex, elementType);


                        break;

                    default:
                        throw new NotSupportedException();
                }
            }

            IList<IIncludePlan> includes = null;
            if (considerSelectors && !(Model.SelectClause.Selector is QuerySourceReferenceExpression))
            {
                var visitor = new SelectorVisitor(this);
                visitor.Visit(Model.SelectClause.Selector);

                includes = visitor.Includes;
            }

            foreach (var resultOperator in queryModel.ResultOperators)
            {
                AddResultOperator(resultOperator);
            }
        }

        public Statement CurrentStatement { get; set; }

        public Statement TopStatement { get; private set; }


        public QueryModel Model { get; }

        private void AddResultOperator(ResultOperatorBase resultOperator)
        {
            switch (resultOperator)
            {
                case ISelectableOperator selectable:
                    CurrentStatement = selectable.ModifyStatement(CurrentStatement, _session);
                    break;

                case TakeResultOperator take:
                    CurrentStatement.Limit = (int)take.Count.Value();
                    break;

                case SkipResultOperator skip:
                    CurrentStatement.Offset = (int)skip.Count.Value();
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

                case IncludeResultOperator _:
                    // TODO -- ignoring this for now, but should do something with it later maybe?
                    break;

                case ToJsonArrayResultOperator _:
                    CurrentStatement.ToJsonSelector();
                    break;

                case LastResultOperator _:
                    throw new InvalidOperationException("Marten does not support Last() or LastOrDefault() queries. Please reverse the ordering and use First()/FirstOrDefault() instead");

                default:
                    throw new NotSupportedException("Don't yet know how to deal with " + resultOperator);
            }
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(QueryStatistics statistics, IList<IIncludePlan> includes)
        {
            BuildDatabaseStatement(statistics, includes);

            var handler = buildHandlerForCurrentStatement<TResult>();

            return includes.Any()
                ? new IncludeQueryHandler<TResult>(handler, includes.Select(x => x.BuildReader(_session)).ToArray())
                : handler;
        }

        public void BuildDatabaseStatement(QueryStatistics statistics, IList<IIncludePlan> includes)
        {
            if (statistics != null)
            {
                CurrentStatement.UseStatistics(statistics);
            }

            TopStatement.CompileStructure(new MartenExpressionParser(_session.Serializer, _session.Options));
        }

        private void wrapIncludes(IList<IIncludePlan> includes)
        {
            // TODO -- not sure if this needs to grab CurrentStatement or higher up
            var statement = new IncludeIdentitySelectorStatement(CurrentStatement, includes, _session);
            TopStatement = statement.Top();
            CurrentStatement = statement.Current();
        }

        private IQueryHandler<TResult> buildHandlerForCurrentStatement<TResult>()
        {
            if (CurrentStatement.SingleValue)
            {
                return CurrentStatement.BuildSingleResultHandler<TResult>(_session, TopStatement);
            }

            return CurrentStatement.SelectClause.BuildHandler<TResult>(_session, TopStatement, CurrentStatement);
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

        public void BuildDiagnosticCommand(FetchType fetchType, CommandBuilder sql)
        {
            switch (fetchType)
            {
                case FetchType.Any:
                    CurrentStatement.ToAny();
                    break;

                case FetchType.Count:
                    CurrentStatement.ToCount<long>();
                    break;

                case FetchType.FetchOne:
                    CurrentStatement.Limit = 1;
                    break;
            }

            TopStatement.CompileStructure(new MartenExpressionParser(_session.Serializer, _session.Options));

            TopStatement.Configure(sql);
        }

        public NpgsqlCommand BuildDatabaseCommand(QueryStatistics statistics, IList<IIncludePlan> plans)
        {
            BuildDatabaseStatement(statistics, plans);

            return _session.BuildCommand(TopStatement);
        }
    }
}
