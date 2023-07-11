using System;
using System.Reflection;

namespace Marten.Linq.Members;

/// <summary>
///     An extension point to "teach" Marten how to support new member types in the Linq support
/// </summary>
public interface IMemberSource
{
    bool TryResolve(string dataLocator, StoreOptions options, ISerializer serializer, Type documentType,
        MemberInfo memberInfo, out IQueryableMember member);
}
