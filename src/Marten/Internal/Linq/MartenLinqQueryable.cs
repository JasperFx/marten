using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Util;
using Marten.Internal.Linq.Includes;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Transforms;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Internal.Linq
{
    public class MartenLinqQueryable<T>: QueryableBase<T>, IMartenQueryable<T>
    {
        private readonly LinqQueryProvider _provider;
        private readonly IMartenSession _session;

        public MartenLinqQueryable(IMartenSession session, LinqQueryProvider provider, Expression expression): base(provider,
            expression)
        {
            _session = session;
            _provider = provider;
        }

        public MartenLinqQueryable(IMartenSession session): base(new LinqQueryProvider(session))
        {
            _session = session;
            _provider = Provider.As<LinqQueryProvider>();
        }

        public MartenLinqQueryable(IMartenSession session, Expression expression): base(new LinqQueryProvider(session),
            expression)
        {
            _session = session;
            _provider = Provider.As<LinqQueryProvider>();
        }

        internal IQueryHandler<TResult> BuildHandler<TResult>(ResultOperatorBase op = null)
        {
            var builder = new LinqHandlerBuilder(_session, Expression, op);
            return builder.BuildHandler<TResult>(Statistics, _provider.Includes);
        }

        public QueryStatistics Statistics
        {
            get => _provider.Statistics;
            set => _provider.Statistics = value;
        }

        public Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<IReadOnlyList<TResult>>(Expression, token);
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            return _provider.ExecuteAsync<bool>(Expression, token, LinqConstants.AnyOperator);
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            return _provider.ExecuteAsync<int>(Expression, token, LinqConstants.CountOperator);
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            return _provider.ExecuteAsync<long>(Expression, token, LinqConstants.LongCountOperator);
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.FirstOperator);
        }

        public Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.FirstOrDefaultOperator);
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SingleOperator);
        }

        public Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SingleOrDefaultOperator);
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SumOperator);
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.MinOperator);
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            return _provider.ExecuteAsync<TResult>(Expression, token, LinqConstants.MaxOperator);
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            return _provider.ExecuteAsync<double>(Expression, token, LinqConstants.AverageOperator);
        }

        public string ToJsonArray()
        {
            return _provider.Execute<string>(Expression, LinqConstants.AsJsonOperator);
        }

        public Task<string> ToJsonArrayAsync(CancellationToken token)
        {
            return _provider.ExecuteAsync<string>(Expression, token, LinqConstants.AsJsonOperator);
        }

        public QueryPlan Explain(FetchType fetchType = FetchType.FetchMany,
            Action<IConfigureExplainExpressions> configureExplain = null)
        {
            var command = ToPreviewCommand(fetchType);

            return _session.Database.ExplainQuery(command, configureExplain);
        }

        public IQueryable<TDoc> TransformTo<TDoc>(string transformName)
        {
            return this.Select(x => x.TransformTo<T, TDoc>(transformName));
        }

       public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        {
            var include = BuildInclude(idSource, callback);
            return this.Select(x => x.IncludePlan(include)).As<IMartenQueryable<T>>();
        }

        internal IIncludePlan BuildInclude<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback)
        {
            var storage = (IDocumentStorage<TInclude>) _session.StorageFor(typeof(TInclude));
            var identityField = _session.StorageFor(typeof(T)).Fields.FieldFor(idSource);

            var include = new IncludePlan<TInclude>(_provider.Includes.Count, storage, identityField, callback);
            return include;
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list)
        {
            return Include<TInclude>(idSource, list.Add);
        }

        internal IIncludePlan BuildInclude<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary)
        {
            var storage = (IDocumentStorage<TInclude>)_session.StorageFor(typeof(TInclude));

            if (storage is IDocumentStorage<TInclude, TKey> s)
            {
                var identityField = _session.StorageFor(typeof(T)).Fields.FieldFor(idSource);

                void Callback(TInclude item)
                {
                    var id = s.Identity(item);
                    dictionary[id] = item;
                }

                return new IncludePlan<TInclude>(_provider.Includes.Count, storage, identityField, Callback);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Id/Document type mismatch. The id type for the included document type {typeof(TInclude).FullNameInCode()} is {storage.IdType.FullNameInCode()}");
            }
        }

        public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary)
        {
            var include = BuildInclude(idSource, dictionary);
            return this.Select(x => x.IncludePlan(include)).As<IMartenQueryable<T>>();
        }



        public IMartenQueryable<T> Stats(out QueryStatistics stats)
        {
            Statistics = new QueryStatistics();
            stats = Statistics;

            return this;
        }

        public NpgsqlCommand ToPreviewCommand(FetchType fetchType)
        {
            var builder = new LinqHandlerBuilder(_session, Expression);
            var command = new NpgsqlCommand();
            var sql = new CommandBuilder(command);
            builder.BuildDiagnosticCommand(fetchType, sql);
            command.CommandText = sql.ToString();
            return command;
        }
    }
}
