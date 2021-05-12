using System;
using System.Reflection;

namespace Marten.Linq.Fields
{
    /// <summary>
    /// An extension point to "teach" Marten how to support new member types in the Linq support
    /// </summary>
    public interface IFieldSource
    {
        bool TryResolve(string dataLocator, StoreOptions options, ISerializer serializer, Type documentType,
            MemberInfo[] members, out IField field);
    }

}
