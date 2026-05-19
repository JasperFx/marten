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

    public static bool IsCandidate(Type idType, [NotNullWhen(true)]out ValueTypeIdGeneration? idGeneration)
    {
        idGeneration = default;
        if (idType == typeof(Type)) return false;
        if (idType == typeof(BigInteger)) return false;

        if (idType.IsGenericType && idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = null;
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

        if (properties.Length == 1)
        {
            var innerProperty = properties[0];
            var identityType = innerProperty.PropertyType;

            var ctor = idType.GetConstructors().FirstOrDefault(x =>
                x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            var dbType = PostgresqlProvider.Instance.GetDatabaseType(identityType, EnumStorage.AsInteger);
            var parameterType = PostgresqlProvider.Instance.TryGetDbType(identityType);

            if (ctor != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new ValueTypeIdGeneration(idType, innerProperty, identityType, ctor);
                return true;
            }

            var builder = idType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x =>
                    x.ReturnType == idType && x.GetParameters().Length == 1 &&
                    x.GetParameters()[0].ParameterType == identityType);

            if (builder != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new ValueTypeIdGeneration(idType, innerProperty, identityType, builder);
                return true;
            }
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
        var target = Expression.Parameter(typeof(object), "target");
        var method = ValueProperty.GetMethod;

        var callGetMethod = Expression.Call(Expression.Convert(target, OuterType), method);

        var lambda = Expression.Lambda<Func<object, T>>(callGetMethod, target);

        return FastExpressionCompiler.ExpressionCompiler.CompileFast(lambda);
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
