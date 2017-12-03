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
            return operationsFor(typeof(T)).OfType<IDeletion>();
        }

        public IEnumerable<IDeletion> DeletionsFor(Type documentType)
        {
            return operationsFor(documentType).OfType<IDeletion>();
        }

        public IEnumerable<object> Updates()
        {
            return _operations.Values.SelectMany(x => x.OfType<UpsertDocument>().Select(u => u.Document))
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

        private IList<IStorageOperation> operationsFor(Type documentType)
        {
            var storageType = _tenant.StorageFor(documentType).TopLevelBaseType;
            return _operations.GetOrAdd(storageType, type => new List<IStorageOperation>());
        }

        public void Patch(PatchOperation patch)
        {
            var list = operationsFor(patch.DocumentType);

            list.Add(patch);
        }

        public void StoreUpserts<T>(params T[] documents)
        {
            var list = operationsFor(typeof(T));

            list.AddRange(documents.Select(x => new UpsertDocument(x)));

        }

        public void StoreUpdates<T>(params T[] documents)
        {
            var list = operationsFor(typeof(T));

            list.AddRange(documents.Select(x => new UpdateDocument(x)));

        }

        public void StoreInserts<T>(params T[] documents)
        {
            var list = operationsFor(typeof(T));

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
                    if (operation is DocumentStorageOperation)
                    {
                        operation.As<DocumentStorageOperation>().Persist(batch, _tenant);
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
                var upsert = _tenant.StorageFor(group.Key);

                group.Each(c => { upsert.RegisterUpdate(null, UpdateStyle.Upsert, batch, c.Document, c.Json); });
            });

            return changes;
        }

        private void writeEvents(UpdateBatch batch)
        {
            var upsert = new EventStreamAppender(_store.Events);
            _events.Values.Each(stream =>
            {
                upsert.RegisterUpdate(batch, stream);
            });
        }

        private IEnumerable<Type> GetTypeDependencies(Type type)
        {
            var mappingFor = _tenant.MappingFor(type);
            var documentMapping = mappingFor as DocumentMapping ?? (mappingFor as SubClassMapping)?.Parent;
            if (documentMapping == null)
                return Enumerable.Empty<Type>();

            return documentMapping.ForeignKeys.Where(x => x.ReferenceDocumentType != type)
                .SelectMany(keyDefinition =>
                {
                    var results = new List<Type>();
                    var referenceMappingType =
                        _tenant.MappingFor(keyDefinition.ReferenceDocumentType) as DocumentMapping;
                    // If the reference type has sub-classes, also need to insert/update them first too
                    if (referenceMappingType != null && referenceMappingType.SubClasses.Any())
                    {
                        results.AddRange(referenceMappingType.SubClasses.Select(s => s.DocumentType));
                    }
                    results.Add(keyDefinition.ReferenceDocumentType);
                    return results;
                });
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
                var list = operationsFor(operation.DocumentType);
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
            return _operations.Values.SelectMany(x => x.OfType<DocumentStorageOperation>()).Any(x => object.ReferenceEquals(entity, x.Document));
        }



        public IEnumerable<T> NonDocumentOperationsOf<T>() where T : IStorageOperation
        {
            return _ancillaryOperations.OfType<T>();
        }


        public bool HasStream(string stream)
        {
            return _events.Values.Any(x => x.Key == stream);
        }

        public EventStream StreamFor(string stream)
        {
            return _events.Values.First(x => x.Key == stream);
        }

        public void Eject<T>(T document)
        {
            var operations = operationsFor(typeof(T));
            var matching = operations.OfType<DocumentStorageOperation>().Where(x => object.ReferenceEquals(document, x.Document)).ToArray();

            foreach (var operation in matching)
            {
                operations.Remove(operation);
            }
           
        }
    }

    public abstract class DocumentStorageOperation : IStorageOperation
    {
        public UpdateStyle UpdateStyle { get; }

        protected DocumentStorageOperation(UpdateStyle updateStyle, object document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            UpdateStyle = updateStyle;
        }

        public Type DocumentType => Document.GetType();

        public object Document { get; }

        public void ConfigureCommand(CommandBuilder builder)
        {
        }

        public void AddParameters(IBatchCommand batch)
        {
        }

        public string TenantOverride { get; set; }


        public bool Persist(UpdateBatch batch, ITenant tenant)
        {
            var upsert = tenant.StorageFor(Document.GetType());
            upsert.RegisterUpdate(TenantOverride, UpdateStyle, batch, Document);

            return true;
        }
    }

    public class UpsertDocument : DocumentStorageOperation
    {
        public UpsertDocument(object document) : base(UpdateStyle.Upsert, document)
        {
        }

        public UpsertDocument(object document, string tenantId) : this(document)
        {
            TenantOverride = tenantId;
        }

        
    }

    public class UpdateDocument : DocumentStorageOperation
    {
        public UpdateDocument(object document) : base(UpdateStyle.Update, document)
        {
        }
    }

    public class InsertDocument : DocumentStorageOperation
    {
        public InsertDocument(object document) : base(UpdateStyle.Insert, document)
        {
        }
    }
}