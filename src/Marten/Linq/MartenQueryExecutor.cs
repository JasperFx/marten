using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryExecutor : IMartenQueryExecutor
    {
        private readonly IQueryParser _parser;
        private readonly IIdentityMap _identityMap;
        private readonly IManagedConnection _runner;
        private readonly IDocumentSchema _schema;
        private readonly MartenExpressionParser _expressionParser;
        private readonly IList<Type> _scalarResultOperators;

        public MartenQueryExecutor(IManagedConnection runner, IDocumentSchema schema, MartenExpressionParser expressionParser, IQueryParser parser, IIdentityMap identityMap)
        {
            _schema = schema;
            _expressionParser = expressionParser;
            _parser = parser;
            _identityMap = identityMap;
            _runner = runner;
            

            _scalarResultOperators = new[]
            {
                typeof (AnyResultOperator),
                typeof (CountResultOperator),
                typeof (LongCountResultOperator),
            };
        }


        private IResolver<T> resolver<T>()
        {
            return _schema.StorageFor(typeof (T)).As<IResolver<T>>();
        }
            
            
        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            if (queryModel.ResultOperators.OfType<AnyResultOperator>().Any())
            {
                var anyCommand = new NpgsqlCommand();
                documentQuery.ConfigureForAny(anyCommand);

                return _runner.Execute(anyCommand, c => (T)c.ExecuteScalar());
            }

            if (queryModel.ResultOperators.OfType<CountResultOperator>().Any())
            {
                var countCommand = new NpgsqlCommand();
                documentQuery.ConfigureForCount(countCommand);

                return _runner.Execute(countCommand, c =>
                {
                    var returnValue = c.ExecuteScalar();
                    return Convert.ToInt32(returnValue).As<T>();
                });
            }

            throw new NotSupportedException();
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var isLast = queryModel.ResultOperators.OfType<LastResultOperator>().Any();
            if (isLast)
            {
                throw new InvalidOperationException("Marten does not support Last()/LastOrDefault() querying. Reverse your ordering and use First()/FirstOrDefault() instead");
            }

            // TODO -- optimize by using Top 1
            var cmd = BuildCommand(queryModel);
            var all = _runner.Resolve(cmd, resolver<T>(), _identityMap).ToArray();

            if (returnDefaultWhenEmpty && all.Length == 0) return default(T);

            return all.Single();
        }


        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            var command = BuildCommand(queryModel);

            if (queryModel.MainFromClause.ItemType == typeof (T))
            {
                return _runner.Resolve(command, resolver<T>(), _identityMap);
            }

            throw new NotSupportedException("Marten does not yet support Select() projections from queryables. Use an intermediate .ToArray() or .ToList() before adding Select() clauses");
        }


        public NpgsqlCommand BuildCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var query = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var command = new NpgsqlCommand();
            query.ConfigureCommand(command);

            return command;
        }

        public NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable)
        {
            var model = _parser.GetParsedQuery(queryable.Expression);
            return BuildCommand(model);
        }

        async Task<IEnumerable<T>> IMartenQueryExecutor.ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            var command = BuildCommand(queryModel);

            if (queryModel.MainFromClause.ItemType == typeof(T))
            {
                return await _runner.ResolveAsync(command, resolver<T>(), _identityMap, token).ConfigureAwait(false);
            }

            throw new NotSupportedException("Marten does not yet support Select() projections from queryables. Use an intermediate .ToArray() or .ToList() before adding Select() clauses");
        }

        public async Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            var scalarResultOperator = queryModel.ResultOperators.SingleOrDefault(x => _scalarResultOperators.Contains(x.GetType()));
            if (scalarResultOperator != null)
            {
                return await ExecuteScalar<T>(scalarResultOperator, queryModel, token).ConfigureAwait(false);
            }

            var choiceResultOperator = queryModel.ResultOperators.OfType<ChoiceResultOperatorBase>().Single();

            var cmd = BuildCommand(queryModel);
            var enumerable = await _runner.ResolveAsync(cmd, resolver<T>(), _identityMap, token).ConfigureAwait(false);
            var all = enumerable.ToArray();

            if (choiceResultOperator.ReturnDefaultWhenEmpty && all.Length == 0)
            {
                return default(T);
            }

            if (choiceResultOperator is LastResultOperator)
            {
                throw new InvalidOperationException("Marten does not support Last()/LastOrDefault(). Use ordering and First()/FirstOrDefault() instead");
            }

            if (choiceResultOperator is SingleResultOperator || choiceResultOperator is FirstResultOperator)
            {
                return all.Single();
            }

            throw new NotSupportedException();
        }

        private Task<T> ExecuteScalar<T>(ResultOperatorBase scalarResultOperator, QueryModel queryModel, CancellationToken token)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            if (scalarResultOperator is AnyResultOperator)
            {
                var anyCommand = new NpgsqlCommand();
                documentQuery.ConfigureForAny(anyCommand);

                return _runner.ExecuteAsync(anyCommand, async(c, tkn) =>
                {
                    var result = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                    return (T)result;
                }, token);
            }

            if (scalarResultOperator is CountResultOperator)
            {
                var countCommand = new NpgsqlCommand();
                documentQuery.ConfigureForCount(countCommand);

                return _runner.ExecuteAsync(countCommand, async(c, tkn) =>
                {
                    var returnValue = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                    return Convert.ToInt32(returnValue).As<T>();
                }, token);
            }

            if (scalarResultOperator is LongCountResultOperator)
            {
                var countCommand = new NpgsqlCommand();
                documentQuery.ConfigureForCount(countCommand);

                return _runner.ExecuteAsync(countCommand, async(c, tkn) =>
                {
                    var returnValue = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                    return Convert.ToInt64(returnValue).As<T>();
                }, token);
            }

            throw new NotSupportedException();
        }
    }
}