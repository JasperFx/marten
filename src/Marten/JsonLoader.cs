using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class JsonLoader : IJsonLoader
    {
        private readonly IManagedConnection _connection;
        private readonly IDocumentSchema _schema;

        public JsonLoader(IManagedConnection connection, IDocumentSchema schema)
        {
            _connection = connection;
            _schema = schema;
        }

        public string FindById<T>(string id) where T : class
        {
            return findJsonById<T>(id);
        }


        public Task<string> FindByIdAsync<T>(string id, CancellationToken token) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        public Task<string> FindJsonByIdAsync<T>(ValueType id, CancellationToken token) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        private string findJsonById<T>(object id)
        {
            var storage = _schema.StorageFor(typeof(T));

            var loader = storage.LoaderCommand(id);
            return _connection.Execute(loader, c => loader.ExecuteScalar() as string);
        }

        private Task<string> findJsonByIdAsync<T>(object id, CancellationToken token)
        {
            var storage = _schema.StorageFor(typeof(T));

            var loader = storage.LoaderCommand(id);
            return _connection.ExecuteAsync(loader, async (conn, executeAsyncToken) =>
            {
                var result = await loader.ExecuteScalarAsync(executeAsyncToken).ConfigureAwait(false);
                return result as string; // Maybe do this as a stream later for big docs?
            }, token);
        }

        public string FindById<T>(int id) where T : class
        {
            return findJsonById<T>(id);
        }

        public string FindById<T>(long id) where T : class
        {
            return findJsonById<T>(id);
        }

        public string FindById<T>(Guid id) where T : class
        {
            return findJsonById<T>(id);
        }

        public Task<string> FindByIdAsync<T>(int id, CancellationToken token = new CancellationToken()) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        public Task<string> FindByIdAsync<T>(long id, CancellationToken token = new CancellationToken()) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        public Task<string> FindByIdAsync<T>(Guid id, CancellationToken token = new CancellationToken()) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }
    }
}