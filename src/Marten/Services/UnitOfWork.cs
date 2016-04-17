using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;

namespace Marten.Services
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IDocumentSchema _schema;
        private readonly ConcurrentDictionary<Type, IEnumerable> _updates = new ConcurrentDictionary<Type, IEnumerable>();
        private readonly ConcurrentDictionary<Type, IEnumerable> _inserts = new ConcurrentDictionary<Type, IEnumerable>();
        private readonly ConcurrentDictionary<Type, IList<Delete>> _deletes = new ConcurrentDictionary<Type, IList<Delete>>(); 
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

        private void delete<T>(object id)
        {
            var list = _deletes.GetOrAdd(typeof (T), _ => new List<Delete>());
            list.Add(new Delete(typeof(T), id));
        }

        public void DeleteEntity<T>(T entity)
        {
            var id = _schema.StorageFor(typeof(T)).Identity(entity);
            var list = _deletes.GetOrAdd(typeof(T), _ => new List<Delete>());
            list.Add(new Delete(typeof(T), id, entity));
        }

        public void Delete<T>(ValueType id)
        {
            delete<T>(id);
        }

        public void Delete<T>(string id)
        {
            delete<T>(id);
        }

        public void StoreUpdates<T>(params T[] documents)
        {
            var list = _updates.GetOrAdd(typeof (T), type => typeof (List<>).CloseAndBuildAs<IEnumerable>(typeof (T))).As<List<T>>();

            list.AddRange(documents);
        }

        public void StoreInserts<T>(params T[] documents)
        {
            var list = _inserts.GetOrAdd(typeof(T), type => typeof(List<>).CloseAndBuildAs<IEnumerable>(typeof(T))).As<List<T>>();

            list.AddRange(documents);
        }

        public IEnumerable<Delete> Deletions()
        {
            return _deletes.Values.SelectMany(x => x);
        }

        public IEnumerable<Delete> DeletionsFor<T>()
        {
            return Deletions().Where(x => x.DocumentType == typeof(T));
        }

        public IEnumerable<Delete> DeletionsFor(Type documentType)
        {
            return Deletions().Where(x => x.DocumentType == documentType);
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
            var documentChanges = GetChanges(batch);
            var changes = new ChangeSet(documentChanges);
            changes.Updated.Fill(Updates());
            changes.Inserted.Fill(Inserts());
            changes.Deleted.AddRange(Deletions());

            return changes;
        }

        public async Task<ChangeSet> ApplyChangesAsync(UpdateBatch batch, CancellationToken token)
        {
            var changes = buildChangeSet(batch);

            await batch.ExecuteAsync(token).ConfigureAwait(false);

            ClearChanges(changes.Changes);

            return changes;
        }

        private DocumentChange[] GetChanges(UpdateBatch batch)
        {
            int index = 0;
            var order = _inserts.Keys.Union(_updates.Keys)
                .TopologicalSort(GetTypeDependencies)
                .ToDictionary(x => x, x => index++);

            _inserts.Keys.OrderBy(type => order[type]).Each(type =>
            {
                var storage = _schema.StorageFor(type);

                _inserts[type].Each(o => storage.RegisterUpdate(batch, o));
            });

            _updates.Keys.OrderBy(type => order[type]).Each(type =>
            {
                var storage = _schema.StorageFor(type);

                _updates[type].Each(o => storage.RegisterUpdate(batch, o));
            });

            _deletes.Keys.Each(type =>
            {
                var storage = _schema.StorageFor(type);
                var mapping = _schema.MappingFor(type);

                _deletes[type].Each(id => id.Configure(_schema.Parser, storage, mapping, batch));
            });

            var changes = detectTrackerChanges();
            changes.GroupBy(x => x.DocumentType).Each(group =>
            {
                var storage = _schema.StorageFor(group.Key);

                group.Each(c =>
                {
                    storage.RegisterUpdate(batch, c.Document, c.Json);
                });
            });

            return changes;
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
            _deletes.Clear();
            _updates.Clear();
            _inserts.Clear();
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

        public void Delete<T>(IWhereFragment where)
        {
            var delete = new Delete(typeof(T), where);
            var list = _deletes.GetOrAdd(typeof(T), _ => new List<Delete>());
            list.Add(delete);
        }
    }
}