using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery : IBatchedQuery
    {
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly IQuerySession _parent;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IDataReaderHandler> _handlers = new List<IDataReaderHandler>(); 

        public BatchedQuery(ICommandRunner runner, IDocumentSchema schema, IIdentityMap identityMap, IQuerySession parent)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
            _parent = parent;
        }

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
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

            _command.AppendQuery($"select {mapping.SelectFields("d")} from {mapping.TableName} as d where id = :{parameter.ParameterName}");

            var handler = new SingleResultReader<T>(source, _schema.StorageFor(typeof(T)), _identityMap);
            _handlers.Add(handler);

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
                var source = new TaskCompletionSource<IList<TDoc>>();

                var mapping = _parent._schema.MappingFor(typeof(TDoc));
                var parameter = _parent._command.AddParameter(keys);
                _parent._command.AppendQuery($"select {mapping.SelectFields("d")} from {mapping.TableName} as d where d.id = ANY(:{parameter.ParameterName})");

                var handler = new MultipleResultsReader<TDoc>(source, _parent._schema.StorageFor(typeof(TDoc)), _parent._identityMap);
                _parent._handlers.Add(handler);

                return source.Task;
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

        public IQueryForExpression<T> Query<T>() where T : class
        {
            return new QueryForExpression<T>();
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