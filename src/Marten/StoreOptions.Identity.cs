using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Microsoft.FSharp.Core;
using CombGuidIdGeneration = Marten.Schema.Identity.CombGuidIdGeneration;

namespace Marten;

public partial class StoreOptions
{
    internal IDocumentStorage<TDoc, TId> ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking tracking)
    {
        var provider = Providers.StorageFor<TDoc>();
        var raw = provider.Select(tracking);

        if (raw is IDocumentStorage<TDoc, TId> storage) return storage;

        var valueTypeInfo = TryFindValueType(raw.IdType);
        if (valueTypeInfo == null)
            throw new InvalidOperationException(
                $"Invalid identifier type for aggregate {typeof(TDoc).FullNameInCode()}. Id type is {raw.IdType.FullNameInCode()}");

        return typeof(ValueTypeIdentifiedDocumentStorage<,,>).CloseAndBuildAs<IDocumentStorage<TDoc, TId>>(
            valueTypeInfo, raw, typeof(TDoc), typeof(TId),
            raw.IdType);
    }


    internal IIdGeneration DetermineIdStrategy(Type documentType, MemberInfo idMember)
    {
        var idType = idMember.GetMemberType();

        if (!idMemberIsSettable(idMember) && !FSharpDiscriminatedUnionIdGeneration.IsFSharpSingleCaseDiscriminatedUnion(idType))
        {
            return new NoOpIdGeneration();
        }

        if (idType == typeof(string))
        {
            return new StringIdGeneration();
        }

        if (idType == typeof(Guid))
        {
            return new CombGuidIdGeneration();
        }

        if (idType == typeof(int) || idType == typeof(long))
        {
            return new HiloIdGeneration(documentType, Advanced.HiloSequenceDefaults);
        }

        if (ValueTypeIdGeneration.IsCandidate(idType, out var valueTypeIdGeneration))
        {
            ValueTypes.Fill(valueTypeIdGeneration);
            return valueTypeIdGeneration;
        }

        if (FSharpDiscriminatedUnionIdGeneration.IsCandidate(idType, out var fSharpDiscriminatedUnionIdGeneration))
        {
            ValueTypes.Fill(fSharpDiscriminatedUnionIdGeneration);
            return fSharpDiscriminatedUnionIdGeneration;
        }

        throw new ArgumentOutOfRangeException(nameof(documentType),
            $"Marten cannot use the type {idType.FullName} as the Id for a persisted document. Use int, long, Guid, or string");
    }

    private bool idMemberIsSettable(MemberInfo idMember)
    {
        if (idMember is FieldInfo f) return f.IsPublic;
        if (idMember is PropertyInfo p) return p.CanWrite && p.SetMethod != null;

        return false;
    }

    internal ValueTypeInfo? TryFindValueType(Type idType)
    {
        return ValueTypes.FirstOrDefault(x => x.OuterType == idType);
    }

    internal ValueTypeInfo FindOrCreateValueType(Type idType)
    {
        var valueType = ValueTypes.FirstOrDefault(x => x.OuterType == idType);
        return valueType ?? RegisterValueType(idType);
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
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FSharpOption<>))
        {
            var innerType = type.GetGenericArguments().Single();
            valueProperty = type.GetProperty("Value");
            var optionBuilder = type.GetMethod("Some", BindingFlags.Static | BindingFlags.Public);
            var valueType = new ValueTypeInfo(type, innerType, valueProperty, optionBuilder);
            ValueTypes.Add(valueType);
            return valueType;
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

    public void RegisterFSharpOptionValueTypes()
    {
        RegisterValueType(typeof(FSharpOption<Guid>));
        RegisterValueType(typeof(FSharpOption<string>));
        RegisterValueType(typeof(FSharpOption<long>));
        RegisterValueType(typeof(FSharpOption<int>));
        RegisterValueType(typeof(FSharpOption<bool>));
        RegisterValueType(typeof(FSharpOption<decimal>));
        RegisterValueType(typeof(FSharpOption<char>));
        RegisterValueType(typeof(FSharpOption<double>));
        RegisterValueType(typeof(FSharpOption<float>));
        RegisterValueType(typeof(FSharpOption<uint>));
        RegisterValueType(typeof(FSharpOption<ulong>));
        RegisterValueType(typeof(FSharpOption<short>));
        RegisterValueType(typeof(FSharpOption<ushort>));
        RegisterValueType(typeof(FSharpOption<DateTime>));
        RegisterValueType(typeof(FSharpOption<DateTimeOffset>));
    }

    internal List<Internal.ValueTypeInfo> ValueTypes { get; } = new();
}
