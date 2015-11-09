using System;
using System.Collections.Generic;

namespace Marten
{
    internal class IdentityMap
    {
        private readonly Dictionary<EntityKey, object> _map = new Dictionary<EntityKey, object>();

        public void Set<T>(T entity)
        {
            var id = GetOrSetId(entity);
            var key = new EntityKey(typeof(T), id);

            if (_map.ContainsKey(key))
            {
                if (!ReferenceEquals(_map[key], entity))
                {
                    throw new InvalidOperationException($"Entity '{typeof(T).FullName}' with same Id already added to the session.");
                }
                return;
            }

            _map[key] = entity;
        }

        private static dynamic GetOrSetId<T>(T entity)
        {
            dynamic dynamicEntity = entity;
            if (dynamicEntity.Id == null)
            {
                dynamicEntity.Id = Guid.NewGuid();
            }
            return dynamicEntity.Id;
        }

        public T Get<T>(object id) where T : class
        {
            object value;
            var key = new EntityKey(typeof(T), id);

            return _map.TryGetValue(key, out value) ? (T) value : default(T);
        }
    }
}