using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;

namespace Marten
{
    internal class JsonLoader: IJsonLoader
    {
        private readonly QuerySession _session;

        public JsonLoader(QuerySession session)
        {
            _session = session;
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
            var storage = _session.StorageFor<T>();
            var handler = new SingleDocumentJsonLoader<T>(storage, id);
            return _session.ExecuteHandler(handler);
        }

        private Task<string> findJsonByIdAsync<T>(object id, CancellationToken token)
        {
            var storage = _session.StorageFor<T>();
            var handler = new SingleDocumentJsonLoader<T>(storage, id);
            return _session.ExecuteHandlerAsync(handler, token);
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
