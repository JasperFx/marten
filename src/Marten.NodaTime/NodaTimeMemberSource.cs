#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Marten.Linq.Members;
using NodaTime;

namespace Marten.NodaTimePlugin;

internal class NodaTimeMemberSource: IMemberSource
{
    public bool TryResolve(IQueryableMember parent, StoreOptions options, MemberInfo memberInfo, Type memberType,
        [NotNullWhen(true)] out IQueryableMember? member)
    {
        if (memberType == typeof(LocalDate) || memberType == typeof(LocalDate?))
        {
            member = new DateOnlyMember(options, parent, options.Serializer().Casing, memberInfo);
            return true;
        }

        if (memberType == typeof(LocalDateTime) || memberType == typeof(LocalDateTime?))
        {
            member = new DateTimeMember(options, parent, options.Serializer().Casing, memberInfo);
            return true;
        }

        if (memberType == typeof(Instant) || memberType == typeof(Instant?)
            || memberType == typeof(ZonedDateTime) || memberType == typeof(ZonedDateTime?)
            || memberType == typeof(OffsetDateTime) || memberType == typeof(OffsetDateTime?))
        {
            member = new NodaTimeTimestampTzMember(options, parent, options.Serializer().Casing, memberInfo);
            return true;
        }

        if (memberType == typeof(LocalTime) || memberType == typeof(LocalTime?))
        {
            member = new TimeOnlyMember(options, parent, options.Serializer().Casing, memberInfo);
            return true;
        }

        member = null;
        return false;
    }
}

/// <summary>
/// Member class for NodaTime types that map to PostgreSQL 'timestamp with time zone'.
/// Uses the mt_immutable_timestamptz wrapper function for index compatibility
/// without the .NET DateTimeOffset-specific casting in comparisons.
/// </summary>
internal class NodaTimeTimestampTzMember: QueryableMember, IComparableMember
{
    public NodaTimeTimestampTzMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member)
        : base(parent, casing, member)
    {
        TypedLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamptz({RawLocator})";
    }

    public override string SelectorForDuplication(string pgType)
    {
        return TypedLocator.Replace("d.data", "data");
    }
}
