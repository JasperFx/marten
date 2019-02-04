using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.Model;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Storage;
using Remotion.Linq;

namespace Marten.Linq
{
    public class MartenQueryExecutor : IQueryExecutor
    {
        private readonly IList<IIncludeJoin> _includes = new List<IIncludeJoin>();

        public MartenQueryExecutor(IManagedConnection runner, DocumentStore store, IIdentityMap identityMap, ITenant tenant)
        {
            IdentityMap = identityMap;
            Tenant = tenant;
            Connection = runner;
            Store = store;
        }

        public IEnumerable<IIncludeJoin> Includes => _includes;

        public IManagedConnection Connection { get; }
        public DocumentStore Store { get; }

        public IIdentityMap IdentityMap { get; }
        public ITenant Tenant { get; }
        public QueryStatistics Statistics { get; set; }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var handler = Store.HandlerFactory.HandlerForScalarQuery<T>(queryModel, Includes.ToArray(),
                Statistics);

            if (handler == null)
                throw new NotSupportedException("Not yet supporting these results: " +
                                                queryModel.AllResultOperators().Select(x => x.GetType().Name).Join(", "));

            return Connection.Fetch(handler, IdentityMap.ForQuery(), Statistics, Tenant);
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var handler = Store.HandlerFactory.HandlerForSingleQuery<T>(queryModel, _includes.ToArray(), Statistics,
                returnDefaultWhenEmpty);

            if (handler == null)
                throw new NotSupportedException("Not yet supporting these results: " +
                                                queryModel.AllResultOperators().Select(x => x.GetType().Name).Join(", "));

            return Connection.Fetch(handler, IdentityMap.ForQuery(), Statistics, Tenant);
        }

        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            Tenant.EnsureStorageExists(queryModel.SourceType());

            var handler = new LinqQuery<T>(Store, queryModel, _includes.ToArray(), Statistics).ToList();

            return Connection.Fetch(handler, IdentityMap.ForQuery(), Statistics, Tenant);
        }

        public void AddInclude(IIncludeJoin include)
        {
            _includes.Add(include);
        }
    }
}