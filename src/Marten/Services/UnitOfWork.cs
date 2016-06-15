using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Patching;
using Marten.Schema;
using Marten.Util;

namespace Marten.Services
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IDocumentSchema _schema;
        private readonly ConcurrentDictionary<Type, IEnumerable> _updates = new ConcurrentDictionary<Type, IEnumerable>();
        private readonly ConcurrentDictionary<Type, IEnumerable> _inserts = new ConcurrentDictionary<Type, IEnumerable>();
        private readonly ConcurrentDictionary<Guid, EventStream> _events = new ConcurrentDictionary<Guid, EventStream>();
        private readonly IList<IStorageOperation> _operations = new List<IStorageOperation>();

        private readonly IList<IDocumentTracker> _trackers = new List<IDocumentTracker>(); 

        public UnitOfWork(IDocumentSchema schema)
        {
            _schema = schema;
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
            _operations.Add(patch);
        }

        public void StoreUpdates<T>(params T[] documents)
        {
            var list = _updates.GetOrAdd(typeof (T), type => new List<T>()).As<List<T>>();

            list.AddRange(documents);
        }

        public void StoreInserts<T>(params T[] documents)
        {
            var list = _inserts.GetOrAdd(typeof(T), type => new List<T>()).As<List<T>>();

            list.AddRange(documents);
        }

        public IEnumerable<IDeletion> Deletions()
        {
            return _operations.OfType<IDeletion>();
        }

        public IEnumerable<IDeletion> DeletionsFor<T>()
        {
            return _operations.OfType<IDeletion>().Where(x => x.DocumentType == typeof(T));
        }

        public IEnumerable<IDeletion> DeletionsFor(Type documentType)
        {
            return _operations.OfType<IDeletion>().Where(x => x.DocumentType == documentType);
        } 

        public IEnumerable<object> Updates()
        {
            return _updates.Values.SelectMany(x => x.OfType<object>())
                .Union(detectTrackerChanges().Select(x => x.Document));
        }

        public IEnumerable<T> UpdatesFor<T>()
        {
            return Updates().OfType<T>();
        }


        public ChangeSet ApplyChanges(UpdateBatch batch)
        {
            ChangeSet changes = buildChangeSet(batch);

            batch.Execute();

            ClearChanges(changes.Changes);

            return changes;
        }

        private ChangeSet buildChangeSet(UpdateBatch batch)
        {
            var documentChanges = determineChanges(batch);
            var changes = new ChangeSet(documentChanges);
            changes.Updated.Fill(Updates());
            changes.Inserted.Fill(Inserts());
            changes.Streams.AddRange(_events.Values);
            changes.Operations.AddRange(_operations);

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
            int index = 0;
            var order = _inserts.Keys.Union(_updates.Keys)
                .TopologicalSort(GetTypeDependencies)
                .ToDictionary(x => x, x => index++);

            _inserts.Keys.OrderBy(type => order[type]).Each(type =>
            {
                var upsert = _schema.UpsertFor(type);

                _inserts[type].Each(o => upsert.RegisterUpdate(batch, o));
            });

            _updates.Keys.OrderBy(type => order[type]).Each(type =>
            {
                var upsert = _schema.UpsertFor(type);

                _updates[type].Each(o => upsert.RegisterUpdate(batch, o));
            });

            writeEvents(batch);


            batch.Add(_operations);

            var changes = detectTrackerChanges();
            changes.GroupBy(x => x.DocumentType).Each(group =>
            {
                var upsert = _schema.UpsertFor(group.Key);

                group.Each(c =>
                {
                    upsert.RegisterUpdate(batch, c.Document, c.Json);
                });
            });

            return changes;
        }

        private void writeEvents(UpdateBatch batch)
        {
            var upsert = _schema.UpsertFor(typeof(EventStream));
            _events.Values.Each(stream => { upsert.RegisterUpdate(batch, stream); });
        }

        private IEnumerable<Type> GetTypeDependencies(Type type)
        {
            var documentMapping = _schema.MappingFor(type) as DocumentMapping;
            if (documentMapping == null)
            {
                return Enumerable.Empty<Type>();
            }

            return documentMapping.ForeignKeys.Select(keyDefinition => keyDefinition.ReferenceDocumentType);
        }

        private DocumentChange[] detectTrackerChanges()
        {
            return _trackers.SelectMany(x => x.DetectChanges()).ToArray();
        }

        private void ClearChanges(DocumentChange[] changes)
        {
            _operations.Clear();
            _updates.Clear();
            _inserts.Clear();
            _events.Clear();
            changes.Each(x => x.ChangeCommitted());
        }

        public IEnumerable<object> Inserts()
        {
            return _inserts.Values.SelectMany(x => x.OfType<object>());
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

    }
}