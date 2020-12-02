using System;
using System.Collections.Generic;

namespace Marten.Services
{
    public class VersionTracker
    {
        private readonly IDictionary<Type, IDictionary<object, Guid>> _versions = new Dictionary<Type, IDictionary<object, Guid>>();

        public void Store<T>(object id, Guid version)
        {
            var documentType = typeof(T);

            Store(documentType, id, version);
        }

        public void Store(Type documentType, object id, Guid version)
        {
            IDictionary<object, Guid> dict;

            if (!_versions.TryGetValue(documentType, out dict))
            {
                dict = new Dictionary<object, Guid>();
                _versions.Add(documentType, dict);
            }

            if (dict.ContainsKey(id))
            {
                dict[id] = version;
            }
            else
            {
                dict.Add(id, version);
            }
        }

        public Guid? Version<T>(object id)
        {
            if (!_versions.TryGetValue(typeof(T), out var dict))
                return null;

            dict.TryGetValue(id, out var guid);
            return guid;
        }

        public void ClearAll()
        {
            _versions.Clear();
        }
    }
}
