using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FastExpressionCompiler;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Schema.Identity;

public class FSharpDiscriminatedUnionIdGeneration: ValueTypeInfo, IIdGeneration, IStrongTypedIdGeneration
{
    private readonly IScalarSelectClause _selector;

    private FSharpDiscriminatedUnionIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType, ConstructorInfo ctor)
        : base(outerType, simpleType, valueProperty, ctor)
    {
        _selector = typeof(FSharpDiscriminatedUnionIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType, SimpleType);
    }

    private FSharpDiscriminatedUnionIdGeneration(Type outerType, PropertyInfo valueProperty, Type simpleType, MethodInfo builder)
        : base(outerType, simpleType, valueProperty, builder)
    {
        _selector = typeof(FSharpDiscriminatedUnionIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, OuterType, SimpleType);
    }

    public IEnumerable<Type> KeyTypes => Type.EmptyTypes;
    public bool RequiresSequences => false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);

        method.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

    public ISelectClause BuildSelectClause(string tableName)
    {
        return _selector.CloneToOtherTable(tableName);
    }

    public static bool IsFSharpSingleCaseDiscriminatedUnion(Type type)
    {
        return type.IsClass && type.IsSealed && type.GetProperties().Any(x => x.Name == "Tag");
    }

    public static bool IsCandidate(Type idType, out FSharpDiscriminatedUnionIdGeneration? idGeneration)
    {

        idGeneration = default;
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
                idGeneration = new FSharpDiscriminatedUnionIdGeneration(idType, innerProperty, identityType, ctor);
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
                idGeneration = new FSharpDiscriminatedUnionIdGeneration(idType, innerProperty, identityType, builder);
                return true;
            }
        }

        return false;
    }

    public string ParameterValue(DocumentMapping mapping)
    {
        if (mapping.IdMember.GetRawMemberType().IsNullable())
        {
            return $"{mapping.IdMember.Name}.Value.{ValueProperty.Name}";
        }

        return $"{mapping.IdMember.Name}.{ValueProperty.Name}";
    }


    public void GenerateCodeForFetchingId(int index, GeneratedMethod sync, GeneratedMethod async,
        DocumentMapping mapping)
    {
        if (Builder != null)
        {
            sync.Frames.Code(
                $"var id = {OuterType.FullNameInCode()}.{Builder.Name}(reader.GetFieldValue<{SimpleType.FullNameInCode()}>({index}));");
            async.Frames.CodeAsync(
                $"var id = {OuterType.FullNameInCode()}.{Builder.Name}(await reader.GetFieldValueAsync<{SimpleType.FullNameInCode()}>({index}, token));");
        }
        else
        {
            sync.Frames.Code(
                $"var id = new {OuterType.FullNameInCode()}(reader.GetFieldValue<{SimpleType.FullNameInCode()}>({index}));");
            async.Frames.CodeAsync(
                $"var id = new {OuterType.FullNameInCode()}(await reader.GetFieldValueAsync<{SimpleType.FullNameInCode()}>({index}, token));");
        }
    }

    public Func<object, T> BuildInnerValueSource<T>()
    {
        var target = Expression.Parameter(typeof(object), "target");
        var method = ValueProperty.GetMethod;

        var callGetMethod = Expression.Call(Expression.Convert(target, OuterType), method);

        var lambda = Expression.Lambda<Func<object, T>>(callGetMethod, target);

        return lambda.CompileFast();
    }

    public void WriteBulkWriterCode(GeneratedMethod load, DocumentMapping mapping)
    {
        var dbType = PostgresqlProvider.Instance.ToParameterType(SimpleType);
        load.Frames.Code($"writer.Write(document.{mapping.IdMember.Name}.{ValueProperty.Name}, {{0}});", dbType);
    }

    public void WriteBulkWriterCodeAsync(GeneratedMethod load, DocumentMapping mapping)
    {
        var dbType = PostgresqlProvider.Instance.ToParameterType(SimpleType);
        load.Frames.Code(
            $"await writer.WriteAsync(document.{mapping.IdMember.Name}.{ValueProperty.Name}, {{0}}, {{1}});",
            dbType, Use.Type<CancellationToken>());
    }
}

internal class FSharpDiscriminatedUnionIdSelectClause<TOuter, TInner>: ISelectClause, IScalarSelectClause, IModifyableFromObject,
    ISelector<TOuter>
{
    public FSharpDiscriminatedUnionIdSelectClause(FSharpDiscriminatedUnionIdGeneration typedIdGeneration)
    {
        Converter = typedIdGeneration.CreateConverter<TOuter, TInner>();
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

    public void Apply(IPostgresqlCommandBuilder sql)
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

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement)
    {
        return (IQueryHandler<TResult>)new ListQueryHandler<TOuter>(statement, this);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TOuter?>(this, statistics);
    }

    public TOuter? Resolve(DbDataReader reader)
    {
        var inner = reader.GetFieldValue<TInner>(0);
        return Converter(inner);
    }

    public async Task<TOuter?> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var inner = await reader.GetFieldValueAsync<TInner>(0, token).ConfigureAwait(false);
        return Converter(inner);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}
