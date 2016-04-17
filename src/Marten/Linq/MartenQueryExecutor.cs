using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq
{
    public class MartenQueryExecutor : IQueryExecutor
    {
        public MartenQueryExecutor(IManagedConnection runner, IDocumentSchema schema, IIdentityMap identityMap)
        {
            Schema = schema;
            IdentityMap = identityMap;
            Connection = runner;
        }

        private readonly IList<IIncludeJoin> _includes = new List<IIncludeJoin>();

        public IEnumerable<IIncludeJoin> Includes => _includes;

        public void AddInclude(IIncludeJoin include)
        {
            _includes.Add(include);
        }

        public IDocumentSchema Schema { get; }

        public IManagedConnection Connection { get; }

        public IIdentityMap IdentityMap { get; }


        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var handler = Schema.HandlerFactory.HandlerForScalarQuery<T>(queryModel);

            if (handler == null)
            {
                throw new NotSupportedException("Not yet supporting these results: " +
                                                queryModel.AllResultOperators().Select(x => x.GetType().Name).Join(", "));
            }

            return Connection.Fetch(handler, IdentityMap.ForQuery());
        }


        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var handler = Schema.HandlerFactory.HandlerForSingleQuery<T>(queryModel, _includes.ToArray(),
                returnDefaultWhenEmpty);

            if (handler == null)
            {
                throw new NotSupportedException("Not yet supporting these results: " +
                                                queryModel.AllResultOperators().Select(x => x.GetType().Name).Join(", "));
            }

            return Connection.Fetch(handler, IdentityMap.ForQuery());
        }

        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            Schema.EnsureStorageExists(queryModel.SourceType());

            var handler = new ListQueryHandler<T>(Schema, queryModel, _includes.ToArray());

            return Connection.Fetch(handler, IdentityMap.ForQuery());
        }

        [Obsolete]
        public NpgsqlCommand BuildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var scalarExecutions = new List<IScalarCommandBuilder<T>>
            {
                new AnyCommandBuilder<T>(Schema.Parser, Schema),
                new CountCommandBuilder<T>(Schema.Parser, Schema),
                new LongCountCommandBuilder<T>(Schema.Parser, Schema),
                new SumCommandBuilder<T>(Schema.Parser, Schema),
                new AverageCommandBuilder<T>(Schema.Parser, Schema),
                new MaxCommandBuilder<T>(Schema.Parser, Schema),
                new MinCommandBuilder<T>(Schema.Parser, Schema)
            };

            NpgsqlCommand cmd;
            var queryExecution = scalarExecutions.FirstOrDefault(x => x.Match(queryModel));
            if (queryExecution != null)
            {
                cmd = queryExecution.BuildCommand(queryModel, out selector);
                return cmd;
            }
            cmd = buildCommand(queryModel, out selector);
            return cmd;
        }

        private NpgsqlCommand buildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var query = Schema.ToDocumentQuery(queryModel);
            query.Includes.AddRange(Includes);

            var command = new NpgsqlCommand();
            selector = query.ConfigureCommand<T>(Schema, command);

            return command;
        }
    }
}