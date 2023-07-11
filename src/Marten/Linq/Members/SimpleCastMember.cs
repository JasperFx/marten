using System.Reflection;

namespace Marten.Linq.Members;

internal class SimpleCastMember: QueryableMember, IComparableMember
{
    private HasValueMember _hasValue;

    public SimpleCastMember(IQueryableMember parent, Casing casing, MemberInfo member, string pgType): base(parent,
        casing, member)
    {
        TypedLocator = $"CAST({RawLocator} as {pgType})";
    }
}
