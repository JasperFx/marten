using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using Marten.Schema;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryExecutor : IMartenQueryExecutor
    {
        private readonly IQueryParser _parser;
        private readonly CommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        public MartenQueryExecutor(IConnectionFactory factory, IDocumentSchema schema, ISerializer serializer,
            IQueryParser parser)
        {
            _schema = schema;
            _serializer = serializer;
            _parser = parser;
            _runner = new CommandRunner(factory);
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            if (queryModel.ResultOperators.OfType<AnyResultOperator>().Any())
            {
                var storage = _schema.StorageFor(queryModel.SelectClause.Selector.Type);
                var anyCommand = storage.AnyCommand(queryModel);

                return _runner.Execute(conn =>
                {
                    anyCommand.Connection = conn;
                    return (T) anyCommand.ExecuteScalar();
                });
            }

            if (queryModel.ResultOperators.OfType<CountResultOperator>().Any())
            {
                var storage = _schema.StorageFor(queryModel.SelectClause.Selector.Type);
                var countCommand = storage.CountCommand(queryModel);

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
            var isFirst = queryModel.ResultOperators.OfType<FirstResultOperator>().Any();

            // TODO -- optimize by returning the count here too?
            var cmd = BuildCommand<T>(queryModel);
            var all = _runner.QueryJson(cmd).ToArray();

            if (returnDefaultWhenEmpty && all.Length == 0) return default(T);

            var data = isFirst ? all.First() : all.Single();

            return _serializer.FromJson<T>(data);
        }


        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            var command = BuildCommand<T>(queryModel);

            return _runner.Query<T>(command, _serializer);
        }

        public NpgsqlCommand BuildCommand<T>(QueryModel queryModel)
        {
            var tableName = _schema.StorageFor(typeof (T)).TableName;
            var query = new DocumentQuery<T>(tableName, queryModel);

            return query.ToCommand();
        }

        public NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable)
        {
            var model = _parser.GetParsedQuery(queryable.Expression);
            return BuildCommand<T>(model);
        }
    }
}