using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
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
        private readonly IMartenSession _session;

        public MartenLinqQueryProvider(IMartenSession session)
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

            return ExecuteHandler(handler);
        }

        public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            var handler = builder.BuildHandler<TResult>();

            return ExecuteHandlerAsync(handler, token);
        }

        public TResult Execute<TResult>(Expression expression, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression, op);
            var handler = builder.BuildHandler<TResult>();

            return ExecuteHandler(handler);
        }

        public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token, ResultOperatorBase op)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression, op);
            var handler = builder.BuildHandler<TResult>();

            return ExecuteHandlerAsync(handler, token);
        }

        public async Task<T> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
        {
            var cmd = _session.BuildCommand(handler);

            using var reader = await _session.Database.ExecuteReaderAsync(cmd, token);
            return await handler.HandleAsync(reader, _session, token);
        }

        public T ExecuteHandler<T>(IQueryHandler<T> handler)
        {
            var cmd = _session.BuildCommand(handler);

            using var reader = _session.Database.ExecuteReader(cmd);
            return handler.Handle(reader, _session);
        }


        public async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(Expression expression, [EnumeratorCancellation]CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            builder.BuildDatabaseStatement();

            var selector = (ISelector<T>)builder.CurrentStatement.SelectClause.BuildSelector(_session);
            var statement = builder.TopStatement;

            var cmd = _session.BuildCommand(statement);

            using var reader = await _session.Database.ExecuteReaderAsync(cmd, token);
            while (await reader.ReadAsync(token))
            {
                yield return await selector.ResolveAsync(reader, token);
            }
        }

        public Task StreamMany(Expression expression, Stream destination, CancellationToken token)
        {
            var builder = BuildLinqHandler(expression);

            var command = builder.TopStatement.BuildCommand();

            return _session.Database.StreamMany(command, destination, token);
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

        public Task<bool> StreamOne(Expression expression, Stream destination, CancellationToken token)
        {
            var builder = new LinqHandlerBuilder(this, _session, expression);
            builder.BuildDatabaseStatement();

            var statement = builder.TopStatement;
            statement.Current().Limit = 1;
            var command = statement.BuildCommand();

            return _session.Database.StreamOne(command, destination, token);
        }
    }
}
