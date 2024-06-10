using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Identity;

public class StrongTypedIdGeneration : IIdGeneration
{
    private readonly MethodInfo _builder;
    private readonly PropertyInfo _innerProperty;
    private readonly ConstructorInfo _ctor;
    public Type IdType { get; }
    public Type SimpleType { get; }

    private StrongTypedIdGeneration(Type idType, PropertyInfo innerProperty, Type simpleType, ConstructorInfo ctor)
    {
        _innerProperty = innerProperty;
        _ctor = ctor;
        IdType = idType;
        SimpleType = simpleType;
    }

    private StrongTypedIdGeneration(Type idType, PropertyInfo innerProperty, Type simpleType, MethodInfo builder)
    {
        IdType = idType;
        _innerProperty = innerProperty;
        _builder = builder;
        SimpleType = simpleType;
    }

    public static bool IsCandidate(Type idType, out IIdGeneration? idGeneration)
    {
        if (idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = default;

        if (!idType.Name.EndsWith("Id")) return false;

        if (!idType.IsPublic && !idType.IsNestedPublic) return false;

        var properties = idType.GetProperties().Where(x => DocumentMapping.ValidIdTypes.Contains(x.PropertyType)).ToArray();
        if (properties.Length == 1)
        {
            var innerProperty = properties[0];
            var identityType = innerProperty.PropertyType;
            if (identityType == typeof(string))
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, "text", NpgsqlDbType.Varchar);

                // TODO -- somehow support the aliased name generation that uses HiLo?
                // Custom generation of the inner values???
                idGeneration = new NoOpIdGeneration();
                return true;
            }

            var ctor = idType.GetConstructors().FirstOrDefault(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            var dbType = PostgresqlProvider.Instance.GetDatabaseType(identityType, EnumStorage.AsInteger);
            var parameterType = PostgresqlProvider.Instance.TryGetDbType(identityType);

            if (ctor != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new StrongTypedIdGeneration(idType, innerProperty, identityType, ctor);
                return true;
            }

            var builder = idType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.ReturnType == idType && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            if (builder != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new StrongTypedIdGeneration(idType, innerProperty, identityType, builder);
                return true;
            }
        }


        return false;
    }

    public IEnumerable<Type> KeyTypes => Type.EmptyTypes;
    public bool RequiresSequences => false;
    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);

        var newGuid = $"{typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid()";
        var create = _ctor == null ? $"{IdType.FullNameInCode()}.{_builder.Name}({newGuid})" : $"new {IdType.FullNameInCode()}({newGuid})";

        method.Frames.Code(
            $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, {create});",
            document);
        method.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

    public string ParameterValue(DocumentMapping mapping)
    {
        return $"{mapping.IdMember.Name}.{_innerProperty.Name}";
    }
}
