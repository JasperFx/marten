using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Util;
using Marten.V4Internals.Linq;
using Npgsql;
using Remotion.Linq.Clauses;

namespace Marten.V4Internals.Sessions
{
    public abstract class MartenSessionBase: IMartenSession, IQueryProvider
    {
        private readonly IPersistenceGraph _persistence;
        private bool _disposed;
        public VersionTracker Versions { get; } = new VersionTracker();
        public IDatabase Database { get; }
        public ISerializer Serializer { get; }
        public Dictionary<Type, object> ItemMap { get; } = new Dictionary<Type, object>();
        public ITenant Tenant { get; }
        public StoreOptions Options { get; }

        protected MartenSessionBase(IDatabase database, ISerializer serializer, ITenant tenant,
            IPersistenceGraph persistence, StoreOptions options)
        {
            _persistence = persistence;
            Database = database;
            Serializer = serializer;
            Tenant = tenant;
            Options = options;
        }

        protected abstract IDocumentStorage<T> selectStorage<T>(DocumentPersistence<T> persistence);


        public IDocumentStorage StorageFor(Type documentType)
        {
            // TODO -- possible optimization opportunity
            return typeof(StorageFinder<>).CloseAndBuildAs<IStorageFinder>(documentType).Find(this);
        }

        private interface IStorageFinder
        {
            IDocumentStorage Find(MartenSessionBase session);
        }

        private class StorageFinder<T>: IStorageFinder
        {
            public IDocumentStorage Find(MartenSessionBase session)
            {
                return session.storageFor<T>();
            }
        }

        protected IDocumentStorage<T, TId> storageFor<T, TId>()
        {
            var storage = storageFor<T>();
            if (storage is IDocumentStorage<T, TId> s) return s;

            throw new InvalidOperationException($"The identity type for {typeof(T).FullName} is {storage.IdType.FullName}, but {typeof(TId).FullName} was used as the Id type");
        }

        protected IDocumentStorage<T> storageFor<T>()
        {
            return selectStorage(_persistence.StorageFor<T>());
        }

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            throw new NotImplementedException();
        }

        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression)
        {
            return new V4Queryable<TElement>(this, expression);
        }

        object IQueryProvider.Execute(Expression expression)
        {

            throw new NotImplementedException();
        }

        public TResult Execute<TResult>(Expression expression)
        {
            var builder = new LinqHandlerBuilder(this, expression);
            var handler = builder.BuildHandler<TResult>();

            // TODO -- worry about QueryStatistics later
            return executeHandler(handler, null);
        }

        public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, expression);
            var handler = builder.BuildHandler<TResult>();

            // TODO -- worry about QueryStatistics later
            return executeHandlerAsync(handler, null, token);
        }

        public TResult Execute<TResult>(Expression expression, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, expression);
            builder.AddResultOperator(op);
            var handler = builder.BuildHandler<TResult>();

            // TODO -- worry about QueryStatistics later
            return executeHandler(handler, null);
        }

        public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, expression);
            builder.AddResultOperator(op);
            var handler = builder.BuildHandler<TResult>();

            // TODO -- worry about QueryStatistics later
            return executeHandlerAsync(handler, null, token);
        }

        protected async Task<T> executeHandlerAsync<T>(IQueryHandler<T> handler, QueryStatistics stats, CancellationToken token)
        {
            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            handler.ConfigureCommand(builder, this);

            cmd.CommandText = builder.ToString();

            using (var reader = await Database.ExecuteReaderAsync(cmd, token).ConfigureAwait(false))
            {
                return await handler.HandleAsync(reader, this, stats, token).ConfigureAwait(false);
            }
        }

        protected T executeHandler<T>(IQueryHandler<T> handler, QueryStatistics stats)
        {
            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            handler.ConfigureCommand(builder, this);

            cmd.CommandText = builder.ToString();

            using (var reader = Database.ExecuteReader(cmd))
            {
                return handler.Handle(reader, this, stats);
            }
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Database?.Dispose();
            GC.SuppressFinalize(this);
        }

        protected void assertNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("This session has been disposed");
        }



    }
}
