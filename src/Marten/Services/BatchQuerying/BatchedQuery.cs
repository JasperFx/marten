using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery : IBatchedQuery
    {
        private static readonly MartenQueryParser _parser = new MartenQueryParser();
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly IQuerySession _parent;
        private readonly ISerializer _serializer;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IDataReaderHandler> _handlers = new List<IDataReaderHandler>();

        public BatchedQuery(ICommandRunner runner, IDocumentSchema schema, IIdentityMap identityMap,
            IQuerySession parent, ISerializer serializer)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
            _parent = parent;
            _serializer = serializer;
        }

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        private void addHandler(IDataReaderHandler handler)
        {
            if (_handlers.Any())
            {
                _handlers.Add(new DataReaderAdvancer(handler));
            }
            else
            {
                _handlers.Add(handler);
            }
        }

        private Task<T> load<T>(object id) where T : class
        {
            if (_identityMap.Has<T>(id))
            {
                return Task.FromResult(_identityMap.Retrieve<T>(id));
            }

            var source = new TaskCompletionSource<T>();

            var mapping = _schema.MappingFor(typeof (T));
            var parameter = _command.AddParameter(id);

            _command.AppendQuery(
                $"select {mapping.SelectFields("d")} from {mapping.TableName} as d where id = :{parameter.ParameterName}");

            var handler = new SingleResultReader<T>(source, _schema.StorageFor(typeof (T)), _identityMap);
            addHandler(handler);

            return source.Task;
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
                var mapping = _parent._schema.MappingFor(typeof (TDoc));
                var parameter = _parent._command.AddParameter(keys);
                _parent._command.AppendQuery(
                    $"select {mapping.SelectFields("d")} from {mapping.TableName} as d where d.id = ANY(:{parameter.ParameterName})");

                var handler = new MultipleResultsReader<TDoc>(_parent._schema.StorageFor(typeof (TDoc)),
                    _parent._identityMap);

                _parent.addHandler(handler);

                return handler.ReturnValue;
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

        public Task<IEnumerable<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            throw new NotImplementedException();
        }

        private DocumentQuery toDocumentQuery<TDoc>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            var queryable = _parent.Query<TDoc>();
            var expression = query(queryable).Expression;

            var model = _parser.GetParsedQuery(expression);

            _schema.EnsureStorageExists(typeof(TDoc));

            return new DocumentQuery(_schema.MappingFor(typeof(TDoc)), model, _serializer);
        }

        private Task<TReturn> addHandler<TDoc, THandler, TReturn>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query) where THandler : IDataReaderHandler<TReturn>, new()
        {
            var model = toDocumentQuery(query);
            var handler = new THandler();
            handler.Configure(_command, model);
            addHandler(handler);

            return handler.ReturnValue;
        }

        public Task<bool> Any<TDoc>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            return addHandler<TDoc, AnyHandler, bool>(query);
        }

        public Task<bool> Any<TDoc>()
        {
            return Any<TDoc>(q => q);
        }

        public Task<long> Count<TDoc>(Func<IQueryable<TDoc>, IQueryable<TDoc>> query)
        {
            return addHandler<TDoc, CountHandler, long>(query);
        }

        public Task<long> Count<TDoc>()
        {
            return Count<TDoc>(q => q);
        }

        public Task<IList<T>> Query<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class
        {
            var documentQuery = toDocumentQuery(query);
            var reader = new QueryHandler<T>(_schema.StorageFor(typeof(T)), _identityMap);

            reader.Configure(_command, documentQuery);

            addHandler(reader);

            return reader.ReturnValue;
        }

        public Task<IList<T>> QueryAll<T>() where T : class
    {
            return Query<T>(q => q);
        }

        public Task<T> First<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class
        {
            var documentQuery = toDocumentQuery<T>(q => query(q).Take(1));
            var reader = new QueryHandler<T>(_schema.StorageFor(typeof(T)), _identityMap);

            reader.Configure(_command, documentQuery);

            addHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.First());
        }

        public Task<T> FirstOrDefault<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class
        {
            var documentQuery = toDocumentQuery<T>(q => query(q).Take(1));
            var reader = new QueryHandler<T>(_schema.StorageFor(typeof(T)), _identityMap);

            reader.Configure(_command, documentQuery);

            addHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.FirstOrDefault());
        }

        public Task<T> Single<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class
        {
            var documentQuery = toDocumentQuery<T>(q => query(q).Take(2));
            var reader = new QueryHandler<T>(_schema.StorageFor(typeof(T)), _identityMap);

            reader.Configure(_command, documentQuery);

            addHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.Single());
        }

        public Task<T> SingleOrDefault<T>(Func<IQueryable<T>, IQueryable<T>> query) where T : class
        {
            var documentQuery = toDocumentQuery<T>(q => query(q).Take(2));
            var reader = new QueryHandler<T>(_schema.StorageFor(typeof(T)), _identityMap);

            reader.Configure(_command, documentQuery);

            addHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.SingleOrDefault());
        }

        public async Task Execute(CancellationToken token = default(CancellationToken))
        {
            await _runner.ExecuteAsync(async (conn, tk) =>
            {
                _command.Connection = conn;
                var reader = await _command.ExecuteReaderAsync(tk);

                foreach (var handler in _handlers)
                {
                    await handler.Handle(reader, tk);
                }

                return 0;
            }, token);
        }
    }
}