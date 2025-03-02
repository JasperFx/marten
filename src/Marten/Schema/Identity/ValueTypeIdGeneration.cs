using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using FastExpressionCompiler;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Identity;

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

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);

        if (SimpleType == typeof(Guid))
        {
            generateGuidWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(int))
        {
            generateIntWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(long))
        {
            generateLongWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(string))
        {
            generateStringWrapper(method, mapping, document);
        }
        else
        {
            throw new NotSupportedException();
        }

        method.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

    private string innerValueAccessor(DocumentMapping mapping)
    {
        return mapping.IdMember.GetRawMemberType().IsNullable() ? $"{mapping.IdMember.Name}.Value" : mapping.IdMember.Name;
    }

    private void generateStringWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        method.Frames.Code($"return {{0}}.{innerValueAccessor(mapping)};", document);
    }

    private void generateLongWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var isDefault = mapping.IdMember.GetRawMemberType().IsNullable() ? $"{mapping.IdMember.Name} == null" : $"{mapping.IdMember.Name}.Value == default";

        var database = Use.Type<IMartenDatabase>();
        if (Ctor != null)
        {
            method.Frames.Code(
                $"if ({{0}}.{isDefault}) _setter({{0}}, new {OuterType.FullNameInCode()}({{1}}.Sequences.SequenceFor({{2}}).NextLong()));",
                document, database, mapping.DocumentType);
        }
        else
        {
            method.Frames.Code(
                $"if ({{0}}.{isDefault}) _setter({{0}}, {OuterType.FullNameInCode()}.{Builder.Name}({{1}}.Sequences.SequenceFor({{2}}).NextLong()));",
                document, database, mapping.DocumentType);
        }
    }

    private void generateIntWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var isDefault =  mapping.IdMember.GetRawMemberType().IsNullable() ? $"{mapping.IdMember.Name} == null" : $"{mapping.IdMember.Name}.Value == default";

        var database = Use.Type<IMartenDatabase>();
        if (Ctor != null)
        {
            method.Frames.Code(
                $"if ({{0}}.{isDefault}) _setter({{0}}, new {OuterType.FullNameInCode()}({{1}}.Sequences.SequenceFor({{2}}).NextInt()));",
                document, database, mapping.DocumentType);
        }
        else
        {
            method.Frames.Code(
                $"if ({{0}}.{isDefault}) _setter({{0}}, {OuterType.FullNameInCode()}.{Builder.Name}({{1}}.Sequences.SequenceFor({{2}}).NextInt()));",
                document, database, mapping.DocumentType);
        }
    }

    private void generateGuidWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var isDefault = mapping.IdMember.GetRawMemberType().IsNullable() ? $"{mapping.IdMember.Name} == null" : $"{mapping.IdMember.Name}.Value == default";

        var newGuid = $"{typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid()";
        var create = Ctor == null
            ? $"{OuterType.FullNameInCode()}.{Builder.Name}({newGuid})"
            : $"new {OuterType.FullNameInCode()}({newGuid})";

        method.Frames.Code(
            $"if ({{0}}.{isDefault}) _setter({{0}}, {create});",
            document);
    }

    public ISelectClause BuildSelectClause(string tableName)
    {
        return _selector.CloneToOtherTable(tableName);
    }

    public static bool IsCandidate(Type idType, out ValueTypeIdGeneration? idGeneration)
    {
        if (idType.IsGenericType && idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = default;
        if (idType.IsClass)
        {
            return false;
        }

        if (!idType.Name.EndsWith("Id"))
        {
            return false;
        }

        if (!idType.IsPublic && !idType.IsNestedPublic)
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

        if (mapping.IdMember.GetRawMemberType().IsNullable())
        {
            load.Frames.Code($"writer.Write(document.{mapping.IdMember.Name}.Value.{ValueProperty.Name}, {{0}});", dbType);
        }
        else
        {
            load.Frames.Code($"writer.Write(document.{mapping.IdMember.Name}.{ValueProperty.Name}, {{0}});", dbType);
        }
    }

    public void WriteBulkWriterCodeAsync(GeneratedMethod load, DocumentMapping mapping)
    {
        var dbType = PostgresqlProvider.Instance.ToParameterType(SimpleType);

        if (mapping.IdMember.GetRawMemberType().IsNullable())
        {
            load.Frames.Code(
                $"await writer.WriteAsync(document.{mapping.IdMember.Name}.Value.{ValueProperty.Name}, {{0}}, {{1}});",
                dbType, Use.Type<CancellationToken>());
        }
        else
        {
            load.Frames.Code(
                $"await writer.WriteAsync(document.{mapping.IdMember.Name}.{ValueProperty.Name}, {{0}}, {{1}});",
                dbType, Use.Type<CancellationToken>());
        }


    }
}

public class ValueTypeIdSelectClause<TOuter, TInner>: ValueTypeSelectClause<TOuter, TInner> where TOuter : struct
{
    public ValueTypeIdSelectClause(ValueTypeIdGeneration idGeneration): base(
        "d.id",
        idGeneration.CreateWrapper<TOuter, TInner>()
    )
    {
    }
}
