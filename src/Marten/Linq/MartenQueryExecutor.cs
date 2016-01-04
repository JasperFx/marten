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
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IList<Type> _scalarResultOperators;

        public MartenQueryExecutor(ICommandRunner runner, IDocumentSchema schema, ISerializer serializer,
            IQueryParser parser)
        {
            _schema = schema;
            _serializer = serializer;
            _parser = parser;
            _runner = runner;
            _scalarResultOperators = new[]
            {
                typeof (AnyResultOperator),
                typeof (CountResultOperator),
                typeof (LongCountResultOperator),
            };
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _serializer);

            _schema.EnsureStorageExists(mapping.DocumentType);

            if (queryModel.ResultOperators.OfType<AnyResultOperator>().Any())
            {
                var anyCommand = documentQuery.ToAnyCommand();

                return _runner.Execute(conn =>
                {
                    anyCommand.Connection = conn;
                    return (T) anyCommand.ExecuteScalar();
                });
            }

            if (queryModel.ResultOperators.OfType<CountResultOperator>().Any())
            {
                var countCommand = documentQuery.ToCountCommand();

                return _runner.Execute(conn =>
                {
                    countCommand.Connection = conn;
                    var returnValue = countCommand.ExecuteScalar();
                    return Convert.ToInt32(returnValue).As<T>();
                });
            }

            throw new NotSupportedException();
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var isLast = queryModel.ResultOperators.OfType<LastResultOperator>().Any();

            // TODO -- optimize by using Top 1
            var cmd = BuildCommand(queryModel);
            var all = _runner.QueryJson(cmd).ToArray();

            if (returnDefaultWhenEmpty && all.Length == 0) return default(T);

            string data = null;
            if (isLast)
            {
                data = all.Last();
            }
            else
            {
                data = all.Single();
            }

            return _serializer.FromJson<T>(data);
        }


        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            var command = BuildCommand(queryModel);

            if (queryModel.MainFromClause.ItemType == typeof (T))
            {
				return _runner.QueryJson(command).Select(_serializer.FromJson<T>);
            }

            throw new NotSupportedException("Marten does not yet support Select() projections from queryables. Use an intermediate .ToArray() or .ToList() before adding Select() clauses");
        }


        public NpgsqlCommand BuildCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var query = new DocumentQuery(mapping, queryModel, _serializer);

            return query.ToCommand();
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
                var queryJsonAsync = await _runner.QueryJsonAsync(command, token);
                return queryJsonAsync.Select(_serializer.FromJson<T>);
            }

            throw new NotSupportedException("Marten does not yet support Select() projections from queryables. Use an intermediate .ToArray() or .ToList() before adding Select() clauses");
        }

        public async Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            var scalarResultOperator = queryModel.ResultOperators.SingleOrDefault(x => _scalarResultOperators.Contains(x.GetType()));
            if (scalarResultOperator != null)
            {
                return await ExecuteScalar<T>(scalarResultOperator, queryModel, token);
            }

            var choiceResultOperator = queryModel.ResultOperators.OfType<ChoiceResultOperatorBase>().Single();

            // TODO -- optimize by using Top 1
            var cmd = BuildCommand(queryModel);
            var enumerable = await _runner.QueryJsonAsync(cmd, token);
            var all = enumerable.ToArray();

            if (choiceResultOperator.ReturnDefaultWhenEmpty && all.Length == 0)
            {
                return default(T);
            }

            string data;
            if (choiceResultOperator is LastResultOperator)
            {
                data = all.Last();
            }
            else if (choiceResultOperator is SingleResultOperator || choiceResultOperator is FirstResultOperator)
            {
                data = all.Single();
            }
            else
            {
                throw new NotSupportedException();
            }

            return _serializer.FromJson<T>(data);
        }

        private async Task<T> ExecuteScalar<T>(ResultOperatorBase scalarResultOperator, QueryModel queryModel, CancellationToken token)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _serializer);

            _schema.EnsureStorageExists(mapping.DocumentType);

            if (scalarResultOperator is AnyResultOperator)
            {
                var anyCommand = documentQuery.ToAnyCommand();

                return await _runner.ExecuteAsync(async (conn, tkn) =>
                {
                    anyCommand.Connection = conn;
                    var result = await anyCommand.ExecuteScalarAsync(tkn);
                    return (T)result;
                }, token);
            }

            if (scalarResultOperator is CountResultOperator)
            {
                var countCommand = documentQuery.ToCountCommand();

                return await _runner.ExecuteAsync(async (conn, tkn) =>
                {
                    countCommand.Connection = conn;
                    var returnValue = await countCommand.ExecuteScalarAsync(tkn);
                    return Convert.ToInt32(returnValue).As<T>();
                }, token);
            }

            if (scalarResultOperator is LongCountResultOperator)
            {
                var countCommand = documentQuery.ToCountCommand();

                return await _runner.ExecuteAsync(async (conn, tkn) =>
                {
                    countCommand.Connection = conn;
                    var returnValue = await countCommand.ExecuteScalarAsync(tkn);
                    return Convert.ToInt64(returnValue).As<T>();
                }, token);
            }

            throw new NotSupportedException();
        }
    }
}