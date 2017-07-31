using System;
using System.IO;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        ISerializer Serializer { get; }

        VersionTracker Versions { get; }

        T Get<T>(object id, TextReader json, long? version);
        T Get<T>(object id, Type concreteType, TextReader json, long? version);

        void Remove<T>(object id);

        void Store<T>(object id, T entity, long? version = null);

        bool Has<T>(object id);

        T Retrieve<T>(object id);

        IIdentityMap ForQuery();
        void ClearChanges();
    }
}