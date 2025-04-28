using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing;
using Marten.Schema.Identity;
using Microsoft.FSharp.Core;
using Newtonsoft.Json.Linq;
using Weasel.Postgresql;

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

        foreach (var source in _linq.allMemberSources())
        {
            if (source.TryResolve(parent, this, member, memberType, out var queryable))
            {
                return queryable;
            }
        }

        if (memberType == typeof(string)) return new StringMember(parent, casing, member);

        // Thank you Newtonsoft. This has to be tested before the IDictionary<,> test
        if (memberType == typeof(JObject))
        {
            return new ChildDocument(this, parent, casing, member);
        }

        if (memberType.Closes(typeof(IDictionary<,>)))
        {
            var fieldType = typeof(DictionaryMember<,>).MakeGenericType(memberType!.GetGenericArguments());
            return (IQueryableMember)Activator.CreateInstance(fieldType, this, parent, casing, member)!;
        }

        if (memberType!.IsEnum)
        {
            return serializer.EnumStorage == Weasel.Core.EnumStorage.AsInteger
                ? new EnumAsIntegerMember(parent, serializer.Casing, member)
                : new EnumAsStringMember(parent, serializer.Casing, member);
        }

        if (memberType == typeof(DateTime) || memberType == typeof(FSharpOption<DateTime>))
        {
            return new DateTimeMember(this, parent, casing, member);
        }

        if (memberType == typeof(DateTimeOffset) || memberType == typeof(FSharpOption<DateTimeOffset>))
        {
            return new DateTimeOffsetMember(this, parent, casing, member);
        }

        if (memberType == typeof(DateOnly) || memberType == typeof(FSharpOption<DateOnly>))
        {
            return new DateOnlyMember(this, parent, casing, member);
        }

        if (memberType == typeof(TimeOnly)  || memberType == typeof(FSharpOption<TimeOnly>) )
        {
            return new TimeOnlyMember(this, parent, casing, member);
        }

        if (isEnumerable(memberType))
        {
            Type? elementType = null;
            try
            {
                elementType = memberType.DetermineElementType()!;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            if (elementType.IsValueTypeForQuerying())
            {
                return new ValueCollectionMember(this, parent, casing, member);
            }

            var valueType = ValueTypes.FirstOrDefault(x => x.OuterType == elementType);
            if (valueType != null)
            {
                return new ValueCollectionMember(this, parent, casing, member);
            }

            return new ChildCollectionMember(this, parent, casing, member, memberType);
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

internal class ValueTypeMemberSource: IMemberSource
{
    public bool TryResolve(IQueryableMember parent, StoreOptions options, MemberInfo memberInfo, Type memberType,
        [NotNullWhen(true)]out IQueryableMember? member)
    {
        var valueType = options.ValueTypes.FirstOrDefault(x => x.OuterType == memberType);

        if (valueType == null)
        {
            member = null;
            return false;
        }

        Type baseType;
        if (valueType.OuterType.IsGenericType && valueType.OuterType.GetGenericTypeDefinition() == typeof(FSharpOption<>))
        {
            baseType = typeof(FSharpOptionValueTypeMember<>).MakeGenericType(valueType.SimpleType);
        }
        else if (valueType.SimpleType == typeof(string))
        {
            baseType = typeof(StringValueTypeMember<>).MakeGenericType(memberType);
        }
        else
        {
            baseType = typeof(ValueTypeMember<,>).MakeGenericType(memberType, valueType.SimpleType);
        }

        member = (IQueryableMember)Activator.CreateInstance(baseType, parent, options.Serializer().Casing, memberInfo, valueType)!;

        return true;
    }
}
