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
            if (!_versions.ContainsKey(typeof(T))) return null;

            var dict = _versions[typeof(T)];

            return dict.ContainsKey(id) ? dict[id] : (Guid?) null;
        }

        public void ClearAll()
        {
            _versions.Clear();
        }

    }
}