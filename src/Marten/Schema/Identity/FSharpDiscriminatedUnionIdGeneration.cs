using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Schema.Identity;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class FSharpDiscriminatedUnionIdGeneration: ValueTypeInfo, IIdGeneration, IStrongTypedIdGeneration
{
    private readonly IScalarSelectClause _selector;

    private FSharpDiscriminatedUnionIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType,
        ConstructorInfo ctor)
        : base(outerType, simpleType, valueProperty, ctor)
    {
        _selector =
            typeof(FSharpDiscriminatedUnionIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType,
                SimpleType);
    }

    private FSharpDiscriminatedUnionIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType,
        MethodInfo builder)
        : base(outerType, simpleType, valueProperty, builder)
    {
        _selector =
            typeof(FSharpDiscriminatedUnionIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType,
                SimpleType);
    }

    public bool IsNumeric => false;

    public ISelectClause BuildSelectClause(string tableName)
    {
        return _selector.CloneToOtherTable(tableName);
    }

    public Func<object, T> BuildInnerValueSource<T>()
    {
        // #5579: emits where the platform allows it, reflects where it does not. See
        // StrongTypedIdValueSource -- the JasperFx 2.80 wrapper fix did not cover this half.
        return StrongTypedIdValueSource.Build<T>(OuterType, ValueProperty);
    }

    public static bool IsFSharpSingleCaseDiscriminatedUnion(Type type)
    {
        return type.IsClass && type.IsSealed && type.GetProperties().Any(x => x.Name == "Tag");
    }

    /// <summary>
    ///     Whether <paramref name="idType" /> has the SHAPE of a single-case DU id, registering nothing
    ///     and constructing nothing. The speculative half of <see cref="IsCandidate" /> (#5562).
    /// </summary>
    public static bool IsCandidateShape(Type idType) => tryMatch(idType, out _, out _, out _);

    public static bool IsCandidate(Type idType,
        [NotNullWhen(true)] out FSharpDiscriminatedUnionIdGeneration? idGeneration)
    {
        idGeneration = default;

        if (!tryMatch(idType, out var innerProperty, out var ctor, out var builder))
        {
            return false;
        }

        var identityType = innerProperty.PropertyType;
        var dbType = PostgresqlProvider.Instance.GetDatabaseType(identityType, EnumStorage.AsInteger);
        var parameterType = PostgresqlProvider.Instance.TryGetDbType(identityType);

        PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);

        idGeneration = ctor != null
            ? new FSharpDiscriminatedUnionIdGeneration(idType, innerProperty, identityType, ctor)
            : new FSharpDiscriminatedUnionIdGeneration(idType, innerProperty, identityType, builder!);

        return true;
    }

    /// <summary>The pure half of <see cref="IsCandidate" />: matches the shape, touches no global state.</summary>
    private static bool tryMatch(
        Type idType,
        [NotNullWhen(true)] out PropertyInfo? innerProperty,
        out ConstructorInfo? ctor,
        out MethodInfo? builder)
    {
        innerProperty = null;
        ctor = null;
        builder = null;

        if (idType.IsClass && !IsFSharpSingleCaseDiscriminatedUnion(idType))
        {
            return false;
        }

        if (!idType.Name.EndsWith("Id"))
        {
            return false;
        }

        var properties = idType.GetProperties()
            .Where(x => x.Name != "Tag")
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
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal class FSharpDiscriminatedUnionIdSelectClause<TOuter, TInner>: ISelectClause, IScalarSelectClause,
    IModifyableFromObject,
    ISelector<TOuter> where TOuter : notnull
{
    public FSharpDiscriminatedUnionIdSelectClause(FSharpDiscriminatedUnionIdGeneration typedIdGeneration)
    {
        Converter = typedIdGeneration.CreateWrapper<TOuter, TInner>();
        MemberName = "d.id";
    }

    public FSharpDiscriminatedUnionIdSelectClause(Func<TInner, TOuter> converter)
    {
        Converter = converter;
    }

    public Func<TInner, TOuter> Converter { get; }

    public string MemberName { get; set; } = "d.id";

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new FSharpDiscriminatedUnionIdSelectClause<TOuter, TInner>(Converter)
        {
            FromObject = tableName, MemberName = MemberName
        };
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(TOuter);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        if (MemberName.IsNotEmpty())
        {
            sql.Append("select ");
            sql.Append(MemberName);
            sql.Append(" as data from ");
        }

        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { MemberName };
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        return (IQueryHandler<TResult>)new ListQueryHandler<TOuter>(statement, this);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TOuter>(this, statistics);
    }

    public TOuter Resolve(DbDataReader reader)
    {
        var inner = reader.GetFieldValue<TInner>(0);
        return Converter(inner);
    }

    public async Task<TOuter> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var inner = await reader.GetFieldValueAsync<TInner>(0, token).ConfigureAwait(false);
        return Converter(inner);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}
