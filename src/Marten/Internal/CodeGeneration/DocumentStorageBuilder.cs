using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Internal.CodeGeneration;

internal class DocumentStorageBuilder
{
    private readonly Type _baseType;
    private readonly DocumentMapping _mapping;
    private readonly Func<DocumentOperations, GeneratedType> _selectorTypeSource;
    private readonly string _typeName;

    public DocumentStorageBuilder(DocumentMapping mapping, StorageStyle style,
        Func<DocumentOperations, GeneratedType> selectorTypeSource)
    {
        _mapping = mapping;
        _selectorTypeSource = selectorTypeSource;
        _typeName = DeriveTypeName(mapping, style);

        _baseType =
            determineOpenDocumentStorageType(style).MakeGenericType(mapping.DocumentType, mapping.IdType);
    }

    public static string DeriveTypeName(DocumentMapping mapping, StorageStyle style)
    {
        return $"{style}{mapping.DocumentType.ToSuffixedTypeName("DocumentStorage")}";
    }

    private Type determineOpenDocumentStorageType(StorageStyle style)
    {
        switch (style)
        {
            case StorageStyle.Lightweight:
                return typeof(LightweightDocumentStorage<,>);

            case StorageStyle.QueryOnly:
                return typeof(QueryOnlyDocumentStorage<,>);

            case StorageStyle.IdentityMap:
                return typeof(IdentityMapDocumentStorage<,>);

            case StorageStyle.DirtyTracking:
                return typeof(DirtyCheckedDocumentStorage<,>);
        }

        throw new NotSupportedException();
    }

    public GeneratedType Build(GeneratedAssembly assembly, DocumentOperations operations)
    {
        var selectorType = _selectorTypeSource(operations);

        return buildDocumentStorageType(assembly, operations, _typeName, _baseType, selectorType);
    }

    private GeneratedType buildDocumentStorageType(GeneratedAssembly assembly, DocumentOperations operations,
        string typeName,
        Type baseType, GeneratedType selectorType)
    {
        var type = assembly.AddType(typeName, baseType);

        writeIdentityMethod(type);
        buildStorageOperationMethods(operations, type);

        type.MethodFor(nameof(ISelectClause.BuildSelector))
            .Frames.Code($"return new {assembly.Namespace}.{selectorType.TypeName}({{0}}, {{1}});",
                Use.Type<IMartenSession>(), Use.Type<DocumentMapping>());

        writeParameterForId(type);
        writeParameterForIdArray(type);
        writeNotImplementedStubs(type);

        return type;
    }

    internal class BuildArrayParameterFrame: SyncFrame
    {
        private readonly ValueTypeIdGeneration _idGeneration;
        private readonly NpgsqlDbType _dbType;
        private readonly string _memberName;

        public BuildArrayParameterFrame(ValueTypeIdGeneration idGeneration)
        {
            _dbType = PostgresqlProvider.Instance.ToParameterType(idGeneration.SimpleType);
            _memberName = idGeneration.ValueProperty.Name;
        }

        public BuildArrayParameterFrame(FSharpDiscriminatedUnionIdGeneration idGeneration)
        {
            _dbType = PostgresqlProvider.Instance.ToParameterType(idGeneration.SimpleType);
            _memberName = idGeneration.ValueProperty.Name;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            var dbTypeCode = $"{typeof(NpgsqlDbType).FullNameInCode()}.Array | {typeof(NpgsqlDbType).FullNameInCode()}.{_dbType}";

            var code = $"return new(){{Value = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ids, x => x.{_memberName})), {nameof(NpgsqlParameter.NpgsqlDbType)} = {dbTypeCode}}};";
            writer.WriteLine(code);
            Next?.GenerateCode(method, writer);
        }
    }

    private void writeParameterForIdArray(GeneratedType type)
    {
        var method = type.MethodFor(nameof(DocumentStorage<string, string>.BuildManyIdParameter));
        if (_mapping.IdStrategy is ValueTypeIdGeneration st)
        {
            method.Frames.Add(new BuildArrayParameterFrame(st));
        }
        else if (_mapping.IdStrategy is FSharpDiscriminatedUnionIdGeneration fst)
        {
            method.Frames.Add(new BuildArrayParameterFrame(fst));
        }
        else
        {
            method.Frames.Code($"return base.{method.MethodName}(ids);");
        }
    }

    private void writeParameterForId(GeneratedType type)
    {
        var method = type.MethodFor(nameof(DocumentStorage<string, string>.RawIdentityValue));
        if (_mapping.IdStrategy is ValueTypeIdGeneration st)
        {
            method.Frames.Code($"return id.{st.ValueProperty.Name};");
        }
        else if (_mapping.IdStrategy is FSharpDiscriminatedUnionIdGeneration fst)
        {
            method.Frames.Code($"return id.{fst.ValueProperty.Name};");
        }
        else
        {
            method.Frames.Code($"return id;");
        }
    }

    private void writeIdentityMethod(GeneratedType type)
    {
        var identity = type.MethodFor("Identity");
        identity.Frames.Code($"return {{0}}.{_mapping.CodeGen.AccessId};", identity.Arguments[0]);

        var assign = type.MethodFor("AssignIdentity");

        _mapping.IdStrategy.GenerateCode(assign, _mapping);
    }


    private static void writeNotImplementedStubs(GeneratedType type)
    {
        var missing = type.Methods.Where(x => !x.Frames.Any()).Select(x => x.MethodName);
        if (missing.Any())
        {
            throw new Exception("Missing methods: " + missing.Join(", "));
        }

        foreach (var method in type.Methods)
        {
            if (!method.Frames.Any())
            {
                method.Frames.ThrowNotImplementedException();
            }
        }
    }

    private void buildStorageOperationMethods(DocumentOperations operations, GeneratedType type)
    {
        if (_mapping.UseOptimisticConcurrency || _mapping.UseNumericRevisions)
        {
            buildConditionalOperationBasedOnConcurrencyChecks(type, operations, "Upsert");
            buildOperationMethod(type, operations, "Insert");
            buildOperationMethod(type, operations, "Overwrite");
            buildConditionalOperationBasedOnConcurrencyChecks(type, operations, "Update");
        }
        else
        {
            buildOperationMethod(type, operations, "Upsert");
            buildOperationMethod(type, operations, "Insert");
            buildOperationMethod(type, operations, "Update");
        }


        if (_mapping.UseOptimisticConcurrency || _mapping.UseNumericRevisions)
        {
            buildOperationMethod(type, operations, "Overwrite");
        }
        else
        {
            type.MethodFor("Overwrite").Frames.ThrowNotSupportedException();
        }
    }

    private void buildConditionalOperationBasedOnConcurrencyChecks(GeneratedType type,
        DocumentOperations operations, string methodName)
    {
        var operationType = (GeneratedType)typeof(DocumentOperations).GetProperty(methodName).GetValue(operations);
        var overwriteType = operations.Overwrite;

        var method = type.MethodFor(methodName);

        method.Frames.Code($"BLOCK:if (session.{nameof(IDocumentSession.Concurrency)} == {{0}})",
            ConcurrencyChecks.Disabled);
        writeReturnOfOperation(method, overwriteType, methodName);
        method.Frames.Code("END");
        method.Frames.Code("BLOCK:else");
        writeReturnOfOperation(method, operationType, methodName);
        method.Frames.Code("END");
    }

    private void buildOperationMethod(GeneratedType type, DocumentOperations operations, string methodName)
    {
        var operationType = (GeneratedType)typeof(DocumentOperations).GetProperty(methodName)!.GetValue(operations)!;
        var method = type.MethodFor(methodName);

        writeReturnOfOperation(method, operationType, methodName);
    }

    private void writeReturnOfOperation(GeneratedMethod method, GeneratedType operationType, string methodName)
    {
        var assembly = method.ParentType.ParentAssembly;

        var tenantDeclaration = "";
        if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
        {
            tenantDeclaration = ", tenant";
        }

        var versionRetriever = _mapping.UseNumericRevisions
            ? "null"
            : $"{{1}}.Versions.ForType<{_mapping.DocumentType.FullNameInCode()}, {_mapping.IdType.FullNameInCode()}>()";


        if (_mapping.IsHierarchy())
        {
            method.Frames
                .Code($@"
return new {assembly.Namespace}.{operationType.TypeName}
(
    {{0}}, Identity({{0}}),
    {versionRetriever},
    {{2}}
    {tenantDeclaration}
);"
                    , new Use(_mapping.DocumentType), Use.Type<IMartenSession>(), Use.Type<DocumentMapping>());
        }
        else
        {
            method.Frames
                .Code($@"
return new {assembly.Namespace}.{operationType.TypeName}
(
    {{0}}, Identity({{0}}),
    {versionRetriever},
    {{2}}
    {tenantDeclaration}
);"
                    , new Use(_mapping.DocumentType), Use.Type<IMartenSession>(), Use.Type<DocumentMapping>());
        }
    }
}
