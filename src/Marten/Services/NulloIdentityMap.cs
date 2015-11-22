using System;

namespace Marten.Services
{
    public class NulloIdentityMap : IIdentityMap
    {
        private readonly ISerializer _serializer;

        public NulloIdentityMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Get<T>(object id, Func<string> json)
        {
            return _serializer.FromJson<T>(json());
        }

        public T Get<T>(object id, string json)
        {
            return _serializer.FromJson<T>(json);
        }

        public void Remove<T>(object id)
        {
            // nothing
        }
    }
}