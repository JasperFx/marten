using System;
using FubuCore;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class DocumentSession : QuerySession, IDocumentSession
    {
        private readonly IIdentityMap _documentMap;
        private readonly ICommandRunner _runner;
        private readonly ISerializer _serializer;
        private readonly IDocumentSchema _schema;
        private readonly UnitOfWork _unitOfWork;

        public DocumentSession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IIdentityMap documentMap) : base(schema, serializer, runner, parser, executor, documentMap)
        {
            _schema = schema;
            _serializer = serializer;
            _runner = runner;

            _documentMap = documentMap;
            _unitOfWork = new UnitOfWork(_schema);

            if (_documentMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(_documentMap.As<IDocumentTracker>());
            }
        }

        public void Dispose()
        {
        }

        public void Delete<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _unitOfWork.Delete(entity);
            _documentMap.Remove<T>(_schema.StorageFor(typeof(T)).Identity(entity));
        }

        public void Delete<T>(ValueType id)
        {
            _unitOfWork.Delete<T>(id);
            _documentMap.Remove<T>(id);
        }

        public void Delete<T>(string id)
        {
            _unitOfWork.Delete<T>(id);
            _documentMap.Remove<T>(id);
        }

        public void Store<T>(T entity) where T : class
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = _schema.StorageFor(typeof(T));
            var id =storage
                .As<IdAssignment<T>>().Assign(entity);

            if (_documentMap.Has<T>(id))
            {
                var existing = _documentMap.Retrieve<T>(id);
                if (!ReferenceEquals(existing, entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof (T).FullName}' with same Id already added to the session.");
                }
            }
            else
            {
                _unitOfWork.Store(entity);
                _documentMap.Store(id, entity);
            }


        }


        public void SaveChanges()
        {
            var batch = new UpdateBatch(_serializer, _runner);
            _unitOfWork.ApplyChanges(batch);

        }

    }
}