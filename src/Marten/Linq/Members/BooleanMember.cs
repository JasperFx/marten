#nullable enable
using System.Reflection;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Core.Serialization;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class BooleanMember: QueryableMember, IComparableMember, IBooleanMember
{
    public BooleanMember(IQueryableMember parent, Casing casing, MemberInfo member, string pgType): base(parent,
        casing, member)
    {
        TypedLocator = $"CAST({RawLocator} as {pgType})";
    }

    public ISqlFragment BuildIsTrueFragment()
    {
        return new BooleanFieldIsTrue(this);
    }
}
