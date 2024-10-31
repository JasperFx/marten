#nullable enable
using System;
using System.Reflection;
using Weasel.Core.Serialization;

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

public class DateOnlyMember: QueryableMember, IComparableMember
{
    public DateOnlyMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member) : base(parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_date({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        return TypedLocator.Replace("d.", "");
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
        return TypedLocator.Replace("d.", "");
    }

}
