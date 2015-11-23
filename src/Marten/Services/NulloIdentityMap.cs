using System;
using FubuCore;

namespace Marten.Services
{
    public class NulloIdentityMap : IIdentityMap
    {
        private readonly ISerializer _serializer;

        public NulloIdentityMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Get<T>(object id, Func<string> json) where T : class
        {
            var text = json();
            if (text.IsEmpty()) return null;

            return _serializer.FromJson<T>(text);
        }

        public T Get<T>(object id, string json) where T : class
        {
            return _serializer.FromJson<T>(json);
        }

        public void Remove<T>(object id)
        {
            // nothing
        }

        public void Store<T>(object id, T entity)
        {
            // nothing
        }

        public bool Has<T>(object id)
        {
            return false;
        }

        public T Retrieve<T>(object id) where T : class
        {
            return null;
        }
    }
}