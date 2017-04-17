using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Patching;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;

namespace Marten.Services
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ConcurrentDictionary<Guid, EventStream> _events = new ConcurrentDictionary<Guid, EventStream>();

        private readonly DocumentStore _store;
        private readonly ITenant _tenant;

        private readonly IList<IDocumentTracker> _trackers = new List<IDocumentTracker>();

        private readonly ConcurrentDictionary<Type, IList<IStorageOperation>> _operations =
            new ConcurrentDictionary<Type, IList<IStorageOperation>>();

        private readonly IList<IStorageOperation> _ancillaryOperations = new List<IStorageOperation>();

        public UnitOfWork(DocumentStore store, ITenant tenant)
        {
            _store = store;
            _tenant = tenant;
        }

        public IEnumerable<IDeletion> Deletions()
        {
            return _operations.Values.SelectMany(x => x).OfType<IDeletion>();
        }

        public IEnumerable<IDeletion> DeletionsFor<T>()
        {
            return _operations.ContainsKey(typeof(T)) 
                ? _operations[typeof(T)].OfType<IDeletion>() 
                : Enumerable.Empty<IDeletion>();
        }

        public IEnumerable<IDeletion> DeletionsFor(Type documentType)
        {
            return _operations.ContainsKey(documentType) 
                ? _operations[documentType].OfType<IDeletion>() 
                : Enumerable.Empty<IDeletion>();
        }

        public IEnumerable<object> Updates()
        {
            return _operations.Values.SelectMany(x => x.OfType<UpdateDocument>().Select(u => u.Document))
                .Union(detectTrackerChanges().Select(x => x.Document));
        }

        public IEnumerable<T> UpdatesFor<T>()
        {
            return Updates().OfType<T>();
        }

        public IEnumerable<object> Inserts()
        {
            return _operations.Values.SelectMany(x => x).OfType<InsertDocument>().Select(x => x.Document);
        }

        public IEnumerable<T> InsertsFor<T>()
        {
            return Inserts().OfType<T>();
        }

        public IEnumerable<T> AllChangedFor<T>()
        {
            return InsertsFor<T>().Union(UpdatesFor<T>());
        }

        public IEnumerable<EventStream> Streams()
        {
            return _events.Values;
        }

        public IEnumerable<PatchOperation> Patches()
        {
            return _operations.OfType<PatchOperation>();
        }

        public void AddTracker(IDocumentTracker tracker)
        {
            _trackers.Fill(tracker);
        }

        public void RemoveTracker(IDocumentTracker tracker)
        {
            _trackers.Remove(tracker);
        }


        public void StoreStream(EventStream stream)
        {
            _events[stream.Id] = stream;
        }

        public bool HasStream(Guid id)
        {
            return _events.ContainsKey(id);
        }

        public EventStream StreamFor(Guid id)
        {
            return _events[id];
        }

        public void Patch(PatchOperation patch)
        {
            var list = _operations.GetOrAdd(patch.DocumentType, type => new List<IStorageOperation>());

            list.Add(patch);
        }

        public void StoreUpdates<T>(params T[] documents)
        {
            var list = _operations.GetOrAdd(typeof(T), type => new List<IStorageOperation>());

            list.AddRange(documents.Select(x => new UpdateDocument(x)));

        }

        public void StoreInserts<T>(params T[] documents)
        {
            var list = _operations.GetOrAdd(typeof(T), type => new List<IStorageOperation>());

            list.AddRange(documents.Select(x => new InsertDocument(x)));
        }


        public ChangeSet ApplyChanges(UpdateBatch batch)
        {
            var changes = buildChangeSet(batch);

            batch.Execute();

            ClearChanges(changes.Changes);

            return changes;
        }

        private ChangeSet buildChangeSet(UpdateBatch batch)
        {
            var documentChanges = determineChanges(batch);
            var changes = new ChangeSet(documentChanges);

            // TODO -- make these be calculated properties on ChangeSet
            changes.Updated.Fill(Updates());
            changes.Inserted.Fill(Inserts());

            changes.Streams.AddRange(_events.Values);
            changes.Operations.AddRange(_operations.Values.SelectMany(x => x));
            changes.Operations.AddRange(_ancillaryOperations);

            return changes;
        }

        public async Task<ChangeSet> ApplyChangesAsync(UpdateBatch batch, CancellationToken token)
        {
            var changes = buildChangeSet(batch);

            await batch.ExecuteAsync(token).ConfigureAwait(false);

            ClearChanges(changes.Changes);

            return changes;
        }

        private DocumentChange[] determineChanges(UpdateBatch batch)
        {
            var types = _operations.Select(x => x.Key).TopologicalSort(GetTypeDependencies);

            foreach (var type in types)
            {
                if (!_operations.ContainsKey(type))
                {
                    continue;
                }

                foreach (var operation in _operations[type])
                {
                    // No Virginia, I do not approve of this but I'm pulling all my hair
                    // out as is trying to make this work
                    if (operation is Upsert)
                    {
                        operation.As<Upsert>().Persist(batch, _tenant);
                    }
                    else
                    {
                        batch.Add(operation);
                    }
                }
            }

            writeEvents(batch);

            batch.Add(_ancillaryOperations);

            var changes = detectTrackerChanges();
            changes.GroupBy(x => x.DocumentType).Each(group =>
            {
                var upsert = _store.Schema.StorageFor(group.Key);

                group.Each(c => { upsert.RegisterUpdate(batch, c.Document, c.Json); });
            });

            return changes;
        }

        private void writeEvents(UpdateBatch batch)
        {
            var upsert = new EventStreamAppender(_store.Schema.Events);
            _events.Values.Each(stream => { upsert.RegisterUpdate(batch, stream); });
        }

        private IEnumerable<Type> GetTypeDependencies(Type type)
        {
            var documentMapping = _store.Schema.MappingFor(type) as DocumentMapping;
            if (documentMapping == null)
                return Enumerable.Empty<Type>();

            return documentMapping.ForeignKeys.Where(x => x.ReferenceDocumentType != type).Select(keyDefinition => keyDefinition.ReferenceDocumentType);
        }

        private DocumentChange[] detectTrackerChanges()
        {
            return _trackers.SelectMany(x => x.DetectChanges()).ToArray();
        }

        public void Add(IStorageOperation operation)
        {
            if (operation.DocumentType == null)
            {
                _ancillaryOperations.Add(operation);
            }
            else
            {
                var list = _operations.GetOrAdd(operation.DocumentType, type => new List<IStorageOperation>());
                list.Add(operation);
            }


        }

        private void ClearChanges(DocumentChange[] changes)
        {
            _operations.Clear();
            _events.Clear();
            changes.Each(x => x.ChangeCommitted());
        }

        public bool HasAnyUpdates()
        {
            return Updates().Any() || _events.Any() || _operations.Any() || _ancillaryOperations.Any();
        }

        public bool Contains<T>(T entity)
        {
            if (_operations.ContainsKey(typeof(T)))
            {
                return _operations[typeof(T)].OfType<Upsert>().Any(x => object.ReferenceEquals(entity, x.Document));
            }

            return false;
        }



        public IEnumerable<T> NonDocumentOperationsOf<T>() where T : IStorageOperation
        {
            return _ancillaryOperations.OfType<T>();
        }
    }

    public abstract class Upsert : IStorageOperation
    {
        protected Upsert(object document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            Document = document;
        }

        public Type DocumentType => Document.GetType();

        public object Document { get; }

        public void ConfigureCommand(CommandBuilder builder)
        {
        }

        public void AddParameters(IBatchCommand batch)
        {
        }

        public bool Persist(UpdateBatch batch, ITenant tenant)
        {
            var upsert = tenant.StorageFor(Document.GetType());
            upsert.RegisterUpdate(batch, Document);

            return true;
        }
    }

    public class UpdateDocument : Upsert
    {
        public UpdateDocument(object document) : base(document)
        {
        }
    }

    public class InsertDocument : Upsert
    {
        public InsertDocument(object document) : base(document)
        {
        }
    }
}