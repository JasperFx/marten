#nullable enable
using System;
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
        var updatedRawLocator = RawLocator.Replace("d.", "", StringComparison.InvariantCulture);
        return TypedLocator.Replace(RawLocator, updatedRawLocator, StringComparison.InvariantCulture);
    }
}

public class DateOnlyMember: QueryableMember, IComparableMember
{
    public DateOnlyMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member) : base(parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_date({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        var updatedRawLocator = RawLocator.Replace("d.", "");
        return TypedLocator.Replace(RawLocator, updatedRawLocator);
    }

}

public class TimeOnlyMember: QueryableMember, IComparableMember
{
    public TimeOnlyMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member) : base(parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_time({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        var updatedRawLocator = RawLocator.Replace("d.", "");
        return TypedLocator.Replace(RawLocator, updatedRawLocator);
    }

}
