using System;
using System.IO;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        ISerializer Serializer { get; }

        VersionTracker Versions { get; }

        T Get<T>(object id, TextReader json, Guid? version);
        T Get<T>(object id, Type concreteType, TextReader json, Guid? version);

        void Remove<T>(object id);
        void RemoveAllOfType(Type type);

        void Store<T>(object id, T entity, Guid? version = null);

        bool Has<T>(object id);

        T Retrieve<T>(object id);

        IIdentityMap ForQuery();
        void ClearChanges();
    }
}