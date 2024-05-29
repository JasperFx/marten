#nullable enable
using System.Reflection;

namespace Marten.Linq.Members;

public class DateTimeMember: QueryableMember, IComparableMember
{
    public DateTimeMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member): base(parent,
        casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamp({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        return TypedLocator.Replace("d.", "");
    }
}
