using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.Model;
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
        public QueryStatistics Statistics { get; set; }


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

            var query = new LinqQuery<T>(queryModel, Schema, _includes.ToArray(), Statistics);

            var handler = new LinqQueryHandler<T>(Schema, queryModel, _includes.ToArray(), Statistics);

            return Connection.Fetch(handler, IdentityMap.ForQuery());
        }

    }
}