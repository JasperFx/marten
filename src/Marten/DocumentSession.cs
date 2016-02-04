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
        private readonly ICommandRunner _runner;
        private readonly ISerializer _serializer;
        private readonly StoreOptions _options;
        private readonly IDocumentSchema _schema;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(StoreOptions options, IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IIdentityMap identityMap) : base(schema, serializer, runner, parser, identityMap)
        {
            _options = options;
            _schema = schema;
            _serializer = serializer;
            _runner = runner;

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

            _unitOfWork.Delete(entity);
            _identityMap.Remove<T>(_schema.StorageFor(typeof(T)).Identity(entity));
        }

        public void Delete<T>(ValueType id)
        {
            _unitOfWork.Delete<T>(id);
            _identityMap.Remove<T>(id);
        }

        public void Delete<T>(string id)
        {
            _unitOfWork.Delete<T>(id);
            _identityMap.Remove<T>(id);
        }

        public void Store<T>(T entity) where T : class
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = _schema.StorageFor(typeof(T));
            var id =storage
                .As<IdAssignment<T>>().Assign(entity);

            if (_identityMap.Has<T>(id))
            {
                var existing = _identityMap.Retrieve<T>(id);
                if (!ReferenceEquals(existing, entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof (T).FullName}' with same Id already added to the session.");
                }
            }
            else
            {
                _identityMap.Store(id, entity);
            }

            _unitOfWork.Store(entity);
        }

        public void SaveChanges()
        {
            var batch = new UpdateBatch(_options, _serializer, _runner);
            _unitOfWork.ApplyChanges(batch);
        }

        public Task SaveChangesAsync(CancellationToken token)
        {
            var batch = new UpdateBatch(_options, _serializer, _runner);
            return _unitOfWork.ApplyChangesAsync(batch, token);
        }
    }
}