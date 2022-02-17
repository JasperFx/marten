using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    internal class MartenLinqQueryProvider: IQueryProvider
    {
        private readonly QuerySession _session;

        public MartenLinqQueryProvider(QuerySession session)
        {
            _session = session;
        }

        internal QueryStatistics Statistics { get; set; }

        internal IList<IIncludePlan> AllIncludes { get; } = new List<IIncludePlan>();

        public IQueryable CreateQuery(Expression expression)
        {
            throw new NotSupportedException();
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new MartenLinqQueryable<TElement>(_session, this, expression);
        }

        public object Execute(Expression expression)
        {
            throw new NotSupportedException();
        }

        public TResult Execute<TResult>(Expression expression)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            var handler = builder.BuildHandler<TResult>();

            ensureStorageExists(builder);

            return ExecuteHandler(handler);
        }

        private void ensureStorageExists(LinqHandlerBuilder builder)
        {
            foreach (var documentType in builder.DocumentTypes())
            {
                _session.Database.EnsureStorageExists(documentType);
            }
        }

        private async ValueTask ensureStorageExistsAsync(LinqHandlerBuilder builder,
            CancellationToken cancellationToken)
        {
            foreach (var documentType in builder.DocumentTypes())
            {
                await _session.Database.EnsureStorageExistsAsync(documentType, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            var handler = builder.BuildHandler<TResult>();

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        public TResult Execute<TResult>(Expression expression, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression, op);
            var handler = builder.BuildHandler<TResult>();

            ensureStorageExists(builder);

            return ExecuteHandler(handler);
        }

        public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression, op);
            var handler = builder.BuildHandler<TResult>();

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

        public async Task<int> StreamJson<TResult>(Stream stream, Expression expression, CancellationToken token, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression, op);
            var handler = builder.BuildHandler<TResult>();

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            var cmd = _session.BuildCommand(handler);

            using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            return await handler.StreamJson(stream, reader, token).ConfigureAwait(false);
        }

        public async Task<T> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
        {
            var cmd = _session.BuildCommand(handler);

            using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            return await handler.HandleAsync(reader, _session, token).ConfigureAwait(false);
        }

        public T ExecuteHandler<T>(IQueryHandler<T> handler)
        {
            var cmd = _session.BuildCommand(handler);

            using var reader = _session.ExecuteReader(cmd);
            return handler.Handle(reader, _session);
        }


        public async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(Expression expression, [EnumeratorCancellation]CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            builder.BuildDatabaseStatement();

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            var selector = (ISelector<T>)builder.CurrentStatement.SelectClause.BuildSelector(_session);
            var statement = builder.TopStatement;

            var cmd = _session.BuildCommand(statement);

            using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                yield return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
            }
        }

        public async Task<int> StreamMany(Expression expression, Stream destination, CancellationToken token)
        {
            var builder = BuildLinqHandler(expression);

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            var command = builder.TopStatement.BuildCommand();

            return await _session.StreamMany(command, destination, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds out a LinqHandlerBuilder for this MartenQueryable<T>
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        internal LinqHandlerBuilder BuildLinqHandler(Expression expression)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            builder.BuildDatabaseStatement();
            return builder;
        }

        public async Task<bool> StreamOne(Expression expression, Stream destination, CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            builder.BuildDatabaseStatement();

            await ensureStorageExistsAsync(builder, token).ConfigureAwait(false);

            var statement = builder.TopStatement;
            statement.Current().Limit = 1;
            var command = statement.BuildCommand();

            return await _session.StreamOne(command, destination, token).ConfigureAwait(false);
        }
    }
}
