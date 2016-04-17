using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public interface ICompiledQueryExecutor
    {
        IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query);
    }

    public class CompiledQueryExecutor : ICompiledQueryExecutor
    {
        private readonly IQueryParser _parser;
        private readonly IDocumentSchema _schema;
        private readonly ConcurrentCache<Type, CachedQuery> _cache = new ConcurrentCache<Type, CachedQuery>();

        public CompiledQueryExecutor(IQueryParser parser, IDocumentSchema schema)
        {
            _parser = parser;
            _schema = schema;
        }

        public IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            var queryType = query.GetType();
            CachedQuery cachedQuery;
            if (!_cache.Has(queryType))
            {
                cachedQuery = buildCachedQuery<TDoc, TOut>(queryType, query.QueryIs());

                _cache[queryType] = cachedQuery;
            }
            else
            {
                cachedQuery = _cache[queryType];
            }

            return cachedQuery.CreateHandler<TOut>(query);
        }


        private CachedQuery buildCachedQuery<TDoc, TOut>(Type queryType, Expression expression)
        {
            var invocation = Expression.Invoke(expression, Expression.Parameter(typeof(IMartenQueryable<TDoc>)));

            var setters = findSetters(queryType, expression);

            var model = MartenQueryParser.Flyweight.GetParsedQuery(invocation);
            _schema.EnsureStorageExists(typeof (TDoc));

            // TODO -- someday we'll add Include()'s to compiled queries
            var handler = _schema.HandlerFactory.BuildHandler<TOut>(model, new IIncludeJoin[0]);
            var cmd = new NpgsqlCommand();
            handler.ConfigureCommand(cmd);

            return new CachedQuery
            {
                Command = cmd,
                ParameterSetters = setters,
                Handler = handler
            };
        }

        private static IList<IDbParameterSetter> findSetters(Type queryType, Expression expression)
        {
            var visitor = new CompiledQueryMemberExpressionVisitor(queryType);
            visitor.Visit(expression);
            var parameterSetters = visitor.ParameterSetters;
            return parameterSetters;
        }

        private class CachedQuery
        {
            private NpgsqlCommand _command;

            public object Handler { get; set; }

            public IList<IDbParameterSetter> ParameterSetters { get; set; }

            public NpgsqlCommand Command
            {
                get { return _command.Clone(); }
                set { _command = value; }
            }

            public IQueryHandler<T> CreateHandler<T>(object model)
            {
                return new CachedQueryHandler<T>(model, Command, Handler.As<IQueryHandler<T>>(), ParameterSetters.ToArray());
            } 
        }

        internal class CachedQueryHandler<T> : IQueryHandler<T>
        {
            private readonly object _model;
            private readonly NpgsqlCommand _template;
            private readonly IQueryHandler<T> _handler;
            private readonly IDbParameterSetter[] _setters;

            public CachedQueryHandler(object model, NpgsqlCommand template, IQueryHandler<T> handler, IDbParameterSetter[] setters)
            {
                _model = model;
                _template = template;
                _handler = handler;
                _setters = setters;
            }

            public Type SourceType => _handler.SourceType;
            public void ConfigureCommand(NpgsqlCommand command)
            {
                var sql = _template.CommandText;
                for (var i = 0; i < _template.Parameters.Count; i++)
                {
                    var param = _setters[i].AddParameter(_model, command);
                    sql = sql.Replace(":" + _template.Parameters[i].ParameterName, ":" + param.ParameterName);
                }

                command.AppendQuery(sql);
            }

            public T Handle(DbDataReader reader, IIdentityMap map)
            {
                return _handler.Handle(reader, map);
            }

            public Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
            {
                return _handler.HandleAsync(reader, map, token);
            }
        }
    }
}