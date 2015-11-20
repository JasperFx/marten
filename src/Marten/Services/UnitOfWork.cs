using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using Marten.Schema;

namespace Marten.Services
{
    public class UnitOfWork
    {
        private readonly IDocumentSchema _schema;
        private readonly ConcurrentDictionary<Type, IEnumerable> _updates = new ConcurrentDictionary<Type, IEnumerable>();
        private readonly ConcurrentDictionary<Type, IList<object>> _deletes = new ConcurrentDictionary<Type, IList<object>>(); 
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
            var list = _deletes.GetOrAdd(typeof (T), _ => new List<object>());
            list.Add(id);
        }

        public void Delete<T>(T entity)
        {
            var id = _schema.StorageFor(typeof(T)).Identity(entity);
            delete<T>(id);
        }

        public void Delete<T>(ValueType id)
        {
            delete<T>(id);
        }

        public void Delete<T>(string id)
        {
            delete<T>(id);
        }

        public void Store<T>(params T[] entities)
        {
            var list = _updates.GetOrAdd(typeof (T), type => typeof (List<>).CloseAndBuildAs<IEnumerable>(typeof (T))).As<List<T>>();

            list.AddRange(entities);
        }


        public void ApplyChanges(UpdateBatch batch)
        {
            _updates.Keys.Each(type =>
            {
                var storage = _schema.StorageFor(type);

                _updates[type].Each(o => storage.RegisterUpdate(batch, o));
            });

            _deletes.Keys.Each(type =>
            {
                var storage = _schema.StorageFor(type);
                var mapping = _schema.MappingFor(type);

                _deletes[type].Each(id => batch.Delete(mapping.TableName, id, storage.IdType));
            });

            var changes = _trackers.SelectMany(x => x.DetectChanges()).ToArray();
            changes.GroupBy(x => x.DocumentType).Each(group =>
            {
                var storage = _schema.StorageFor(group.Key);

                group.Each(c =>
                {
                    storage.RegisterUpdate(batch, c.Document, c.Json);
                });
            });

            batch.Execute();

            _deletes.Clear();
            _updates.Clear();
            changes.Each(x => x.ChangeCommitted());
        }

        
    }
}