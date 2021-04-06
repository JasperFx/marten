using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration.Util;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Transforms;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;

#nullable enable
namespace Marten.Linq
{
    internal class MartenLinqQueryable<T>: QueryableBase<T>, IMartenQueryable<T>
    {
        private readonly IMartenSession _session;

        public MartenLinqQueryable(IMartenSession session, MartenLinqQueryProvider provider, Expression expression): base(provider,
            expression)
        {
            _session = session;
            MartenProvider = provider;
        }

        public MartenLinqQueryable(IMartenSession session): base(new MartenLinqQueryProvider(session))
        {
            _session = session;
            MartenProvider = Provider.As<MartenLinqQueryProvider>();
        }

        public MartenLinqQueryable(IMartenSession session, Expression expression): base(new MartenLinqQueryProvider(session),
            expression)
        {
            _session = session;
            MartenProvider = Provider.As<MartenLinqQueryProvider>();
        }

        public MartenLinqQueryProvider MartenProvider { get; }

        internal IQueryHandler<TResult> BuildHandler<TResult>(ResultOperatorBase? op = null)
        {
            var builder = new LinqHandlerBuilder(MartenProvider, _session, Expression, op);
            return builder.BuildHandler<TResult>();
        }

        public QueryStatistics Statistics
        {
            get => MartenProvider.Statistics;
            set => MartenProvider.Statistics = value;
        }

        public Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<IReadOnlyList<TResult>>(Expression, token);
        }

        public IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken token = default)
        {
            return MartenProvider.ExecuteAsyncEnumerable<T>(Expression, token);
        }

        public Task StreamManyAsync(Stream destination, CancellationToken token)
        {
            return MartenProvider.StreamMany(Expression, destination, token);
        }

        public Task<bool> StreamOne(Stream destination, CancellationToken token)
        {
            return MartenProvider.StreamOne(Expression, destination, token);
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<bool>(Expression, token, LinqConstants.AnyOperator);
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<int>(Expression, token, LinqConstants.CountOperator);
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<long>(Expression, token, LinqConstants.LongCountOperator);
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.FirstOperator);
        }

        public Task<TResult?> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.FirstOrDefaultOperator)!;
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SingleOperator);
        }

        public Task<TResult?> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SingleOrDefaultOperator)!;
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.SumOperator);
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.MinOperator);
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<TResult>(Expression, token, LinqConstants.MaxOperator);
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<double>(Expression, token, LinqConstants.AverageOperator);
        }

        public string ToJsonArray()
        {
            return MartenProvider.Execute<string>(Expression, LinqConstants.AsJsonOperator);
        }

        public Task<string> ToJsonArrayAsync(CancellationToken token)
        {
            return MartenProvider.ExecuteAsync<string>(Expression, token, LinqConstants.AsJsonOperator);
        }

        public Task<string> ToJsonArrayAsync()
        {
            return MartenProvider.ExecuteAsync<string>(Expression, CancellationToken.None, LinqConstants.AsJsonOperator);
        }

        public QueryPlan Explain(FetchType fetchType = FetchType.FetchMany,
            Action<IConfigureExplainExpressions>? configureExplain = null)
        {
            var command = ToPreviewCommand(fetchType);

            return _session.Database.ExplainQuery(_session.Serializer, command, configureExplain)!;
        }

        public IQueryable<TDoc> TransformTo<TDoc>(string transformName)
        {
            return this.Select(x => x.TransformTo<T, TDoc>(transformName));
        }

       public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : notnull
        {
            var include = BuildInclude(idSource, callback);
            MartenProvider.AllIncludes.Add(include);
            return this;
        }

        internal IIncludePlan BuildInclude<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : notnull
        {
            var storage = (IDocumentStorage<TInclude>) _session.StorageFor(typeof(TInclude));
            var identityField = _session.StorageFor(typeof(T)).Fields.FieldFor(idSource);

            var include = new IncludePlan<TInclude>(storage, identityField, callback);
            return include;
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list) where TInclude : notnull
        {
            return Include<TInclude>(idSource, list.Add);
        }

        internal IIncludePlan BuildInclude<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey: notnull
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

                return new IncludePlan<TInclude>(storage, identityField, Callback);
            }
            else
            {
                throw new DocumentIdTypeMismatchException(storage, typeof(TKey));
            }
        }

        public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary) where TInclude : notnull where TKey : notnull
        {
            var include = BuildInclude(idSource, dictionary);
            MartenProvider.AllIncludes.Add(include);
            return this;
        }



        public IMartenQueryable<T> Stats(out QueryStatistics stats)
        {
            Statistics = new QueryStatistics();
            stats = Statistics;

            return this;
        }

        public NpgsqlCommand ToPreviewCommand(FetchType fetchType)
        {
            var builder = new LinqHandlerBuilder(MartenProvider, _session, Expression);
            var command = new NpgsqlCommand();
            var sql = new CommandBuilder(command);
            builder.BuildDiagnosticCommand(fetchType, sql);
            command.CommandText = sql.ToString();
            return command;
        }
    }
}
