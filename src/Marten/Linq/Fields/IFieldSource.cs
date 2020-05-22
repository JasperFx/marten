using System;
using System.Reflection;

namespace Marten.Linq.Fields
{
    public interface IFieldSource
    {
        bool TryResolve(string dataLocator, StoreOptions options, ISerializer serializer, Type documentType,
            MemberInfo[] members, out IField field);
    }

}
