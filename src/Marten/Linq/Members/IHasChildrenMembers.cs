#nullable enable
using System.Reflection;

namespace Marten.Linq.Members;

public interface IHasChildrenMembers
{
    IQueryableMember FindMember(MemberInfo member);
    void ReplaceMember(MemberInfo member, IQueryableMember queryableMember);
}
