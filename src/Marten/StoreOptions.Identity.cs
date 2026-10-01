using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImTools;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Weasel.Core;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class StoreOptions
{
    internal IDocumentStorage<TDoc, TId> ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking tracking) where TDoc : notnull where TId : notnull
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
        var idType = idMember.GetMemberType()!;

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
            return new SequentialGuidIdGeneration();
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
    /// to use this type correctly within LINQ expressions. The "TValueType"
    /// should wrap a single, primitive value with a single public get-able
    /// property
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public ValueTypeInfo RegisterValueType<TValueType>() where TValueType : notnull
    {
        return RegisterValueType(typeof(TValueType));
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
        else if (FSharpTypeHelper.IsFSharpOptionType(type))
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
            var valueType = new ValueTypeInfo(type, valueProperty.PropertyType, valueProperty, ctor);
            registerValueTypeMapping(valueType);
            ValueTypes.Add(valueType);
            return valueType;
        }

        var candidateBuilders = type.GetMethods(BindingFlags.Static | BindingFlags.Public).Where(x =>
        {
            var parameters = x.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == valueProperty.PropertyType;
        }).ToArray();

        var builder = candidateBuilders.FirstOrDefault(x => x.ReturnType == type)
                      ?? candidateBuilders.FirstOrDefault();

        if (builder != null)
        {
            var valueType = new ValueTypeInfo(type, valueProperty.PropertyType, valueProperty, builder);
            registerValueTypeMapping(valueType);
            ValueTypes.Add(valueType);
            return valueType;
        }

        throw new InvalidValueTypeException(type,
            "Unable to determine either a builder static method or a constructor to use");
    }

    /// <summary>
    ///     Teach the Postgres provider how a value type's column is typed, so a duplicated field or a
    ///     parameter built from it can infer an NpgsqlDbType.
    /// </summary>
    /// <remarks>
    ///     #5562. This used to happen only as a SIDE EFFECT of the strong-typed-id probe, which ran over
    ///     every property and field of every mapped type and registered anything shaped like a
    ///     strong-typed id. A value type registered here and used as a duplicated field rather than as an
    ///     id therefore worked only because some document happened to carry it where the probe reached
    ///     it. Narrowing that probe to the chosen id member would have taken this with it, so the
    ///     registration now belongs to the call that registers the value type -- which is where a reader
    ///     would look for it anyway.
    ///
    ///     F# option types are deliberately excluded, as they were before: neither probe ever accepted
    ///     one (ValueTypeIdGeneration rejects classes, and the DU probe requires a name ending in "Id"),
    ///     so registering them here would be a new behaviour rather than a preserved one.
    /// </remarks>
    private static void registerValueTypeMapping(ValueTypeInfo valueType)
    {
        var dbType = PostgresqlProvider.Instance.GetDatabaseType(valueType.SimpleType, EnumStorage.AsInteger);
        var parameterType = PostgresqlProvider.Instance.TryGetDbType(valueType.SimpleType);

        PostgresqlProvider.Instance.RegisterMapping(valueType.OuterType, dbType, parameterType);
    }

    public void RegisterFSharpOptionValueTypes()
    {
        if (!FSharpTypeHelper.IsFSharpCoreAvailable) return;

        var innerTypes = new[]
        {
            typeof(Guid), typeof(string), typeof(long), typeof(int), typeof(bool),
            typeof(decimal), typeof(char), typeof(double), typeof(float),
            typeof(uint), typeof(ulong), typeof(short), typeof(ushort),
            typeof(DateTime), typeof(DateTimeOffset)
        };

        foreach (var inner in innerTypes)
        {
            var optionType = FSharpTypeHelper.MakeFSharpOptionType(inner);
            if (optionType != null) RegisterValueType(optionType);
        }
    }

    internal List<ValueTypeInfo> ValueTypes { get; } = new();
}
