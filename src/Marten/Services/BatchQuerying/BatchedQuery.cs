using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.Results;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        void Configure(IDocumentSchema schema, NpgsqlCommand command);

        // TODO -- THIS REALLY, REALLY needs to be async all the way down
        void Read(DbDataReader reader, IIdentityMap map);


    }

    public class BatchQueryItem<T> : IBatchQueryItem
    {
        private readonly IQueryHandler<T> _handler;

        public BatchQueryItem(IQueryHandler<T> handler)
        {
            _handler = handler;

            Completion = new TaskCompletionSource<T>();
        }


        public TaskCompletionSource<T> Completion { get; }
        public Task<T> Result => Completion.Task;

        public void Configure(IDocumentSchema schema, NpgsqlCommand command)
        {
            _handler.ConfigureCommand(schema, command);
        }

        public void Read(DbDataReader reader, IIdentityMap map)
        {
            // TODO -- do async, all the way through
            var result = _handler.Handle(reader, map);
            Completion.SetResult(result);
        }
    }



    public class BatchedQuery : IBatchedQuery
    {
        private static readonly MartenQueryParser QueryParser = new MartenQueryParser();
        private readonly IManagedConnection _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly QuerySession _parent;
        private readonly ISerializer _serializer;
        private readonly MartenExpressionParser _parser;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();

        public BatchedQuery(IManagedConnection runner, IDocumentSchema schema, IIdentityMap identityMap, QuerySession parent, ISerializer serializer, MartenExpressionParser parser)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
            _parent = parent;
            _serializer = serializer;
            _parser = parser;
        }

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> AddItem<T>(IQueryHandler<T> handler)
        {
            var item = new BatchQueryItem<T>(handler);
            _items.Add(item);

            item.Configure(_schema, _command);

            return item.Result;
        }

        private Task<T> load<T>(object id) where T : class
        {
            throw new NotImplementedException("NWO");
            /*
            if (_identityMap.Has<T>(id))
            {
                return Task.FromResult(_identityMap.Retrieve<T>(id));
            }

            var source = new TaskCompletionSource<T>();

            var mapping = _schema.MappingFor(typeof (T));
            var parameter = _command.AddParameter(id);

            _command.AppendQuery(
                $"select {mapping.SelectFields().Join(", ")} from {mapping.QualifiedTableName} as d where id = :{parameter.ParameterName}");

            var handler = new SingleResultReader<T>(source, _schema.StorageFor(typeof (T)), _identityMap);
            AddItem(handler);

            return source.Task;
            */
        }


        public IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class
        {
            return new BatchLoadByKeys<TDoc>(this);
        }

        public class BatchLoadByKeys<TDoc> : IBatchLoadByKeys<TDoc> where TDoc : class
        {
            private readonly BatchedQuery _parent;

            public BatchLoadByKeys(BatchedQuery parent)
            {
                _parent = parent;
            }

            private Task<IList<TDoc>> load<TKey>(TKey[] keys)
            {
                throw new NotImplementedException();
                /*
                var mapping = _parent._schema.MappingFor(typeof (TDoc));
                var parameter = _parent._command.AddParameter(keys);
                _parent._command.AppendQuery(
                    $"select {mapping.SelectFields().Join(", ")} from {mapping.QualifiedTableName} as d where d.id = ANY(:{parameter.ParameterName})");

                var resolver = _parent._schema.StorageFor(typeof (TDoc)).As<IResolver<TDoc>>();

                var handler = new MultipleResultsReader<TDoc>(new WholeDocumentSelector<TDoc>(mapping, resolver), _parent._identityMap);

                _parent.AddItem(handler);

                return handler.ReturnValue;
                */
            }

            public Task<IList<TDoc>> ById<TKey>(params TKey[] keys)
            {
                return load(keys);
            }

            public Task<IList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
            {
                return load(keys.ToArray());
            }
        }

        public Task<IList<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            throw new NotImplementedException();

            /*
            _parent.ConfigureCommand<T>(_command, sql, parameters);

            var handler = new QueryResultsReader<T>(_serializer);
            AddItem(handler);

            return handler.ReturnValue;
            */
        }

        private DocumentQuery toDocumentQuery<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            var expression = queryable.Expression;

            var model = QueryParser.GetParsedQuery(expression);

            _schema.EnsureStorageExists(model.MainFromClause.ItemType);

            var docQuery = new DocumentQuery(_schema.MappingFor(model.MainFromClause.ItemType), model, _parser);
            docQuery.Includes.AddRange(queryable.Includes);
            

            return docQuery;
        }


        public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddItem(new AnyQueryHandler<TDoc>(toDocumentQuery(queryable)));
        }


        public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddItem(new CountQueryHandler<long>(toDocumentQuery(queryable)));
        }

        internal Task<IList<T>> Query<T>(IMartenQueryable<T> queryable)
        {
            var documentQuery = toDocumentQuery(queryable);
            return AddItem(new ListQueryHandler<T>(documentQuery));
        }

        public IBatchedQueryable<T> Query<T>() where T : class
        {
            return new BatchedQueryable<T>(this, _parent.Query<T>());
        }


        public Task<T> First<T>(IMartenQueryable<T> queryable)
        {
            return AddItem(new FirstHandler<T>(toDocumentQuery(queryable)));
        }

        public Task<T> FirstOrDefault<T>(IMartenQueryable<T> queryable)
        {
            return AddItem(new FirstOrDefaultHandler<T>(toDocumentQuery(queryable)));
        }

        public Task<T> Single<T>(IMartenQueryable<T> queryable)
        {
            return AddItem(new SingleHandler<T>(toDocumentQuery(queryable)));
        }

        public Task<T> SingleOrDefault<T>(IMartenQueryable<T> queryable)
        {
            return AddItem(new SingleOrDefaultHandler<T>(toDocumentQuery(queryable)));
        }

        public Task Execute(CancellationToken token = default(CancellationToken))
        {
            var map = _identityMap.ForQuery();

            return _runner.ExecuteAsync(_command, async (cmd, tk) =>
            {
                using (var reader = await _command.ExecuteReaderAsync(tk).ConfigureAwait(false))
                {
                    // TODO -- this will be async later
                    _items[0].Read(reader, map);

                    var others = _items.Skip(1).ToArray();

                    foreach (var item in others)
                    {
                        var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

                        if (!hasNext)
                        {
                            throw new InvalidOperationException("There is no next result to read over.");
                        }

                        // TODO -- needs to be purely async later
                        item.Read(reader, map);
                    }

                }

                return 0;
            }, token);
        }
    }
}