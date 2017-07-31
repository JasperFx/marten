using System;
using System.Collections.Generic;

namespace Marten.Services
{
    public class VersionTracker
    {
        private readonly IDictionary<Type, IDictionary<object, long>> _versions = new Dictionary<Type, IDictionary<object, long>>();
        
        
        public void Store<T>(object id, long version)
        {
            IDictionary<object, long> dict;
            if (!_versions.TryGetValue(typeof(T), out dict))
            {
                dict = new Dictionary<object, long>();
                _versions.Add(typeof(T), dict);
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

        public long? Version<T>(object id)
        {
            if (!_versions.ContainsKey(typeof(T))) return null;

            var dict = _versions[typeof(T)];

            return dict.ContainsKey(id) ? dict[id] : (long?) null;
        }

        public void ClearAll()
        {
            _versions.Clear();
        }

    }
}