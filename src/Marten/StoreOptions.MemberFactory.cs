using System;
using System.Collections.Generic;
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
// START HERE. CHECK IF THE ELEMENT TYPE IS AN ID FOR ANY KNOWN DOCUMENT
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

    /// <summary>
    /// Register a custom value type with Marten. Doing this enables Marten
    /// to use this type correctly within LINQ expressions. The "value type"
    /// should wrap a single, primitive value with a single public get-able
    /// property
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public ValueTypeInfo RegisterValueType(Type type)
    {
        PropertyInfo? valueProperty;
        if (FSharpDiscriminatedUnionIdGeneration.IsFSharpSingleCaseDiscriminatedUnion(type))
        {
            valueProperty = type.GetProperties().Where(x => x.Name != "Tag").SingleOrDefaultIfMany();
        }
        else
        {
            valueProperty = type.GetProperties().SingleOrDefaultIfMany();
        }

        if (valueProperty == null || !valueProperty.CanRead) throw new InvalidValueTypeException(type, "Must be only a single public, 'gettable' property");

        var ctor = type.GetConstructors()
            .FirstOrDefault(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == valueProperty.PropertyType);

        if (ctor != null)
        {
            var valueType = new Internal.ValueTypeInfo(type, valueProperty.PropertyType, valueProperty, ctor);
            ValueTypes.Add(valueType);
            return valueType;
        }

        var builder = type.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(x =>
            x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == valueProperty.PropertyType);

        if (builder != null)
        {
            var valueType = new ValueTypeInfo(type, valueProperty.PropertyType, valueProperty, builder);
            ValueTypes.Add(valueType);
            return valueType;
        }

        throw new InvalidValueTypeException(type,
            "Unable to determine either a builder static method or a constructor to use");
    }

    internal List<Internal.ValueTypeInfo> ValueTypes { get; } = new();
}

internal class ValueTypeMemberSource: IMemberSource
{
    public bool TryResolve(IQueryableMember parent, StoreOptions options, MemberInfo memberInfo, Type memberType,
        out IQueryableMember? member)
    {
        var valueType = options.ValueTypes.FirstOrDefault(x => x.OuterType == memberType);
        if (valueType == null)
        {
            member = default;
            return false;
        }

        var baseType = valueType.SimpleType == typeof(string)
            ? typeof(StringValueTypeMember<>).MakeGenericType(memberType)
            : typeof(ValueTypeMember<,>).MakeGenericType(memberType, valueType.SimpleType);

        member = (IQueryableMember)Activator.CreateInstance(baseType, parent, options.Serializer().Casing, memberInfo, valueType);

        return true;
    }
}
