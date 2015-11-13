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
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        public MartenQueryExecutor(ICommandRunner runner, IDocumentSchema schema, ISerializer serializer,
            IQueryParser parser)
        {
            _schema = schema;
            _serializer = serializer;
            _parser = parser;
            _runner = runner;
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel);

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
            var isFirst = queryModel.ResultOperators.OfType<FirstResultOperator>().Any();

            // TODO -- optimize by using Top 1
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
            var mapping = _schema.MappingFor(typeof (T));
            var query = new DocumentQuery(mapping, queryModel);

            return query.ToCommand();
        }

        public NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable)
        {
            var model = _parser.GetParsedQuery(queryable.Expression);
            return BuildCommand<T>(model);
        }
    }
}