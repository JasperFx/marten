using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IIdentityMap _identityMap;
        private readonly IManagedConnection _connection;
        private readonly ISerializer _serializer;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(StoreOptions options, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap) : base(schema, serializer, connection, parser, identityMap)
        {
            _options = options;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;

            _identityMap = identityMap;
            _unitOfWork = new UnitOfWork(_schema);

            if (_identityMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(_identityMap.As<IDocumentTracker>());
            }

        }

        public void Delete<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _unitOfWork.DeleteEntity(entity);

            var storage = _schema.StorageFor(typeof (T));
            storage.Remove(_identityMap, entity);
        }

        public void Delete<T>(ValueType id)
        {
            _unitOfWork.Delete<T>(id);

            var storage = _schema.StorageFor(typeof(T));
            storage.Delete(_identityMap, id);
        }

        public void Delete<T>(string id)
        {
            _unitOfWork.Delete<T>(id);

            var storage = _schema.StorageFor(typeof(T));
            storage.Delete(_identityMap, id);
        }

        public void Store<T>(params T[] entities) where T : class
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            var storage = _schema.StorageFor(typeof(T));
            var idAssignment = storage.As<IdAssignment<T>>();

            foreach (var entity in entities)
            {
                var id = idAssignment.Assign(entity);

                storage.Store(_identityMap, id, entity);
                _unitOfWork.Store(entity);
            }
        }

        public void SaveChanges()
        {
            var batch = new UpdateBatch(_options, _serializer, _connection);
            _unitOfWork.ApplyChanges(batch);

            _connection.Commit();
        }

        public Task SaveChangesAsync(CancellationToken token)
        {
            var batch = new UpdateBatch(_options, _serializer, _connection);
            return _unitOfWork.ApplyChangesAsync(batch, token).ContinueWith(t => _connection.Commit(), token);
        }
    }
}