using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Identity;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class ValueTypeIdGeneration: ValueTypeInfo, IIdGeneration, IStrongTypedIdGeneration
{
    private readonly IScalarSelectClause _selector;

    private ValueTypeIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType, ConstructorInfo ctor)
        : base(outerType, simpleType, valueProperty, ctor)
    {
        _selector = typeof(ValueTypeIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType,
            SimpleType);
    }

    private ValueTypeIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType, MethodInfo builder)
        : base(outerType, simpleType, valueProperty, builder)
    {
        _selector = typeof(ValueTypeIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType,
            SimpleType);
    }

    public bool IsNumeric => false;

    public ISelectClause BuildSelectClause(string tableName)
    {
        return _selector.CloneToOtherTable(tableName);
    }

    /// <summary>
    ///     Whether <paramref name="idType" /> has the SHAPE of a strong-typed id, without registering
    ///     anything or constructing anything.
    /// </summary>
    /// <remarks>
    ///     <see cref="IsCandidate" /> cannot be used to ask the question speculatively: it registers the
    ///     type on the global <see cref="PostgresqlProvider" /> singleton and closes an open generic to
    ///     build the select clause. Identity resolution has to test many members to find one, so it asks
    ///     this instead and only lets <see cref="IsCandidate" /> near the member it actually picked
    ///     (#5562).
    /// </remarks>
    public static bool IsCandidateShape(Type idType) => tryMatch(idType, out _, out _, out _, out _);

    public static bool IsCandidate(Type idType, [NotNullWhen(true)]out ValueTypeIdGeneration? idGeneration)
    {
        idGeneration = default;

        if (!tryMatch(idType, out var outerType, out var innerProperty, out var ctor, out var builder))
        {
            return false;
        }

        var identityType = innerProperty.PropertyType;
        var dbType = PostgresqlProvider.Instance.GetDatabaseType(identityType, EnumStorage.AsInteger);
        var parameterType = PostgresqlProvider.Instance.TryGetDbType(identityType);

        PostgresqlProvider.Instance.RegisterMapping(outerType, dbType, parameterType);

        idGeneration = ctor != null
            ? new ValueTypeIdGeneration(outerType, innerProperty, identityType, ctor)
            : new ValueTypeIdGeneration(outerType, innerProperty, identityType, builder!);

        return true;
    }

    /// <summary>
    ///     The pure half of <see cref="IsCandidate" />: decides whether the type is a strong-typed id and
    ///     hands back the pieces, touching no global state.
    /// </summary>
    private static bool tryMatch(
        Type idType,
        out Type outerType,
        [NotNullWhen(true)] out PropertyInfo? innerProperty,
        out ConstructorInfo? ctor,
        out MethodInfo? builder)
    {
        outerType = idType;
        innerProperty = null;
        ctor = null;
        builder = null;

        if (idType == typeof(Type)) return false;
        if (idType == typeof(BigInteger)) return false;

        if (idType.IsGenericType && idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
            outerType = idType;
        }

        if (idType.IsClass)
        {
            return false;
        }

        if (!idType.IsPublic && !idType.IsNestedPublic)
        {
            return false;
        }

        // Reject multi-property value objects (e.g. `Money(decimal Amount, Guid CurrencyId)`)
        // before any further matching. Such types' canonical constructors take more than one
        // argument; a strong-typed-id wrapper takes at most one. Without this guard the
        // ValidIdTypes filter below would silently drop the non-id-typed property, see only
        // the Guid one, match a static `Zero(Guid)` builder, and — as a side effect — register
        // the entire value object as a `uuid` column on the global PostgresqlProvider singleton,
        // breaking LINQ resolution for any document that uses it as a property.
        if (idType.GetConstructors().Any(c => c.GetParameters().Length > 1))
        {
            return false;
        }

        var properties = idType.GetProperties()
            .Where(x => DocumentMapping.ValidIdTypes.Contains(x.PropertyType))
            .ToArray();

        if (properties.Length != 1)
        {
            return false;
        }

        var candidate = properties[0];
        var identityType = candidate.PropertyType;

        ctor = idType.GetConstructors().FirstOrDefault(x =>
            x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

        if (ctor != null)
        {
            innerProperty = candidate;
            return true;
        }

        builder = idType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(x =>
                x.ReturnType == idType && x.GetParameters().Length == 1 &&
                x.GetParameters()[0].ParameterType == identityType);

        if (builder != null)
        {
            innerProperty = candidate;
            return true;
        }

        return false;
    }

    public string ParameterValue(DocumentMapping mapping)
    {
        if (mapping.IdMember.GetRawMemberType()!.IsNullable())
        {
            return $"{mapping.IdMember.Name}.Value.{ValueProperty.Name}";
        }

        return $"{mapping.IdMember.Name}.{ValueProperty.Name}";
    }

    public Func<object, T> BuildInnerValueSource<T>()
    {
        // #5579: emits where the platform allows it, reflects where it does not. See
        // StrongTypedIdValueSource -- the JasperFx 2.80 wrapper fix did not cover this half.
        return StrongTypedIdValueSource.Build<T>(OuterType, ValueProperty);
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
public class ValueTypeIdSelectClause<TOuter, TInner>: ValueTypeSelectClause<TOuter, TInner> where TOuter : struct
{
    public ValueTypeIdSelectClause(ValueTypeIdGeneration idGeneration): base(
        "d.id",
        idGeneration.CreateWrapper<TOuter, TInner>()
    )
    {
    }
}
