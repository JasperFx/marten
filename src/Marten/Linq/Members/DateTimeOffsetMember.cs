#nullable enable
using System.Reflection;
using Weasel.Core.Serialization;

namespace Marten.Linq.Members;

public class DateTimeOffsetMember: QueryableMember, IComparableMember
{
    public DateTimeOffsetMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member): base(
        parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamptz({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        return TypedLocator.Replace("d.", "");
    }
}
