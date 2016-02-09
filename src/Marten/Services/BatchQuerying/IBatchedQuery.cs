using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchedQuery
    {
        Task<T> Load<T>(string id) where T : class;
        Task<T> Load<T>(ValueType id) where T : class;

        IBatchLoadByKeys<TDoc> Load<TDoc>() where TDoc : class;


        Task<IEnumerable<T>> Query<T>(string sql, params object[] parameters) where T : class;


        IQueryForExpression<T> Query<T>() where T : class;


        Task Execute(CancellationToken token = default(CancellationToken));

    }

    public class BatchedQuery : IBatchedQuery
    {
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IDataReaderHandler> _handlers = new List<IDataReaderHandler>(); 

        public BatchedQuery(ICommandRunner runner, IDocumentSchema schema, IIdentityMap identityMap)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
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
            appendSql($"select {mapping.SelectFields("d")} from {mapping.TableName} as d where id = :{parameter.ParameterName}");

            var handler = new SingleResultReader<T>(source, _schema.StorageFor(typeof(T)), _identityMap);
            _handlers.Add(handler);

            return source.Task;
        }

        private void appendSql(string sql)
        {
            if (_command.CommandText.IsEmpty())
            {
                _command.CommandText = sql;
            }
            else
            {
                _command.CommandText += ";" + sql;
            }
        }


        public IBatchLoadByKeys<TDoc> Load<TDoc>() where TDoc : class
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            throw new NotImplementedException();
        }

        public IQueryForExpression<T> Query<T>() where T : class
        {
            throw new NotImplementedException();
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