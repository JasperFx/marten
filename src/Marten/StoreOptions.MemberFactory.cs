using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing;
using Newtonsoft.Json.Linq;
using Weasel.Postgresql;

#nullable enable

namespace Marten;

public partial class StoreOptions
{
    internal IQueryableMember CreateQueryableMember(MemberInfo member, IQueryableMember parent)
    {
        if (member == null)
        {
            throw new ArgumentNullException(nameof(member));
        }

        var memberType = member.GetMemberType();

        return CreateQueryableMember(member, parent, memberType);
    }

    internal IQueryableMember CreateQueryableMember(MemberInfo member, IQueryableMember parent, Type? memberType)
    {
        if (memberType == null)
            throw new ArgumentOutOfRangeException(nameof(member), $"Cannot determine member type for {member}");

        var serializer = Serializer();
        var casing = serializer.Casing;

        if (memberType == typeof(string)) return new StringMember(parent, casing, member);

        // Thank you Newtonsoft. This has to be tested before the IDictionary<,> test
        if (memberType == typeof(JObject))
        {
            return new ChildDocument(this, parent, casing, member);
        }

        if (memberType.Closes(typeof(IDictionary<,>)))
        {
            var fieldType = typeof(DictionaryMember<,>).MakeGenericType(memberType!.GetGenericArguments());
            return (IQueryableMember)Activator.CreateInstance(fieldType, parent, casing, member)!;
        }

        if (memberType!.IsEnum)
        {
            return serializer.EnumStorage == Weasel.Core.EnumStorage.AsInteger
                ? new EnumAsIntegerMember(parent, serializer.Casing, member)
                : new EnumAsStringMember(parent, serializer.Casing, member);
        }

        if (memberType == typeof(DateTime))
        {
            return new DateTimeMember(this, parent, casing, member);
        }

        if (memberType == typeof(DateTimeOffset))
        {
            return new DateTimeOffsetMember(this, parent, casing, member);
        }

        if (isEnumerable(memberType))
        {
            var elementType = memberType.DetermineElementType();

            if (elementType.IsValueTypeForQuerying())
            {
                return new ValueCollectionMember(this, parent, casing, member);
            }

            return new ChildCollectionMember(this, parent, casing, member);
        }

        var pgType = PostgresqlProvider.Instance.GetDatabaseType(memberType, serializer.EnumStorage);

        if (pgType.EqualsIgnoreCase("jsonb"))
        {
            return new ChildDocument(this, parent, casing, member);
        }

        if (pgType == "boolean")
        {
            return new BooleanMember(parent, casing, member, "boolean");
        }

        if (pgType.IsNotEmpty())
        {
            return new SimpleCastMember(parent, casing, member, pgType);
        }

        throw new NotSupportedException("Just no there yet for fields of type " + memberType.FullNameInCode());
    }

    private static bool isEnumerable(Type fieldType)
    {
        return fieldType.IsArray || fieldType.Closes(typeof(IEnumerable<>));
    }
}
