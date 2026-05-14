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
using Weasel.Postgresql;

namespace Marten;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class StoreOptions
{
    internal IQueryableMember CreateQueryableMember(MemberInfo member, IQueryableMember parent)
    {
        ArgumentNullException.ThrowIfNull(member);

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

        // 9.0: opaque-container types registered via the optional Marten.Newtonsoft
        // package land here (e.g. JObject). Must be tested before the IDictionary<,>
        // check below because JObject implements IDictionary<string, JToken> and would
        // otherwise be misrouted to DictionaryMember.
        if (ChildDocumentTypes.Contains(memberType))
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

        if (memberType == typeof(DateTime) || memberType == FSharpTypeHelper.MakeFSharpOptionType(typeof(DateTime)))
        {
            return new DateTimeMember(this, parent, casing, member);
        }

        if (memberType == typeof(DateTimeOffset) || memberType == FSharpTypeHelper.MakeFSharpOptionType(typeof(DateTimeOffset)))
        {
            return new DateTimeOffsetMember(this, parent, casing, member);
        }

        if (memberType == typeof(DateOnly) || memberType == FSharpTypeHelper.MakeFSharpOptionType(typeof(DateOnly)))
        {
            return new DateOnlyMember(this, parent, casing, member);
        }

        if (memberType == typeof(TimeOnly)  || memberType == FSharpTypeHelper.MakeFSharpOptionType(typeof(TimeOnly)) )
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

[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
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

        if (valueType.OuterType.IsGenericType
            && valueType.OuterType.GetGenericTypeDefinition() == FSharpTypeHelper.GetFSharpOptionOpenType()
            && isSpecialFSharpOptionDateType(valueType.SimpleType))
        {
            member = null;
            return false;
        }

        Type baseType;
        if (valueType.OuterType.IsGenericType && valueType.OuterType.GetGenericTypeDefinition() == FSharpTypeHelper.GetFSharpOptionOpenType())
        {
            var fsharpOptionType = FSharpTypeHelper.MakeFSharpOptionType(valueType.SimpleType)!;
            baseType = typeof(ValueTypeMember<,>).MakeGenericType(fsharpOptionType, valueType.SimpleType);
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

    private static bool isSpecialFSharpOptionDateType(Type simpleType)
    {
        return simpleType == typeof(DateTime)
               || simpleType == typeof(DateTimeOffset)
               || simpleType == typeof(DateOnly)
               || simpleType == typeof(TimeOnly);
    }
}
