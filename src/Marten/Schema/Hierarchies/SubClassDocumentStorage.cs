using System;
using System.Collections.Generic;
using System.Data.Common;
using Baseline;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema.Hierarchies
{
    public class SubClassDocumentStorage<T, TBase> 
        : IDocumentStorage, IResolver<T>, IBulkLoader<T>, IdAssignment<T>
        where T : class, TBase
        where TBase : class 
    {
        private readonly IDocumentStorage _parent;
        private readonly IdAssignment<TBase> _parentIdAssignment;
        private readonly IBulkLoader<TBase> _parentBulkLoader;

        public SubClassDocumentStorage(IDocumentStorage parent)
        {
            _parent = parent;
            _parentIdAssignment = _parent.As<IdAssignment<TBase>>();
            _parentBulkLoader = _parent.As<IBulkLoader<TBase>>();
        }

        public Type DocumentType => typeof (T);
        public NpgsqlDbType IdType => _parent.IdType;
        public NpgsqlCommand LoaderCommand(object id)
        {
            return _parent.LoaderCommand(id);
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return _parent.DeleteCommandForId(id);
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return _parent.DeleteCommandForEntity(entity);
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            return _parent.LoadByArrayCommand(ids);
        }

        public object Identity(object document)
        {
            return _parent.Identity(document);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            _parent.RegisterUpdate(batch, entity);
        }

        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            _parent.RegisterUpdate(batch, entity, json);
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var id = reader[0];
            var json = reader.GetString(1);
            return map.Get<TBase>(id, typeof(T), json) as T;
        }

        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
        {
            _parentBulkLoader.Load(serializer, conn, documents);
        }

        public object Assign(T document)
        {
            return _parentIdAssignment.Assign(document);
        }
    }
}