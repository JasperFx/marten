using System;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.V4Internals.Linq;

namespace Marten.V4Internals
{
    internal class DocumentStorageBuilder
    {
        private readonly Func<DocumentOperations, GeneratedType> _selectorTypeSource;
        private Type _baseType;
        private string _typeName;
        private DocumentMapping _mapping;

        public DocumentStorageBuilder(DocumentMapping mapping, StorageStyle style, Func<DocumentOperations, GeneratedType> selectorTypeSource)
        {
            _mapping = mapping;
            _selectorTypeSource = selectorTypeSource;
            _typeName = $"{style}{mapping.DocumentType.NameInCode()}DocumentStorage";

            _baseType =
                determineOpenDocumentStorageType(style).MakeGenericType(mapping.DocumentType, mapping.IdType);
        }

        private Type determineOpenDocumentStorageType(StorageStyle style)
        {
            switch (style)
            {
                case StorageStyle.Lightweight:
                    return typeof(LightweightDocumentStorage<,>);

                case StorageStyle.IdentityMap:
                    return typeof(IdentityMapDocumentStorage<,>);

                case StorageStyle.QueryOnly:
                    return typeof(QueryOnlyDocumentStorage<,>);

                case StorageStyle.DirtyTracking:
                    return typeof(DirtyTrackingDocumentStorage<,>);
            }

            throw new NotSupportedException();
        }

        public GeneratedType Build(GeneratedAssembly assembly, DocumentOperations operations)
        {
            var selectorType = _selectorTypeSource(operations);

            return buildDocumentStorageType(assembly, operations, _typeName, _baseType, selectorType);
        }

        private GeneratedType buildDocumentStorageType(GeneratedAssembly assembly, DocumentOperations operations, string typeName,
            Type baseType, GeneratedType selectorType)
        {
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);
            buildStorageOperationMethods(operations, type);

            type.MethodFor(nameof(ISelectClause.BuildSelector))
                .Frames.Code($"return new Marten.Generated.{selectorType.TypeName}({{0}}, {{1}});",
                    Use.Type<IMartenSession>(), Use.Type<DocumentMapping>());

            buildLoaderCommands(type);
            writeNotImplementedStubs(type);


            return type;
        }

        private void writeIdentityMethod(GeneratedType type)
        {
            var identity = type.MethodFor("Identity");
            identity.Frames.Code($"return {{0}}.{_mapping.IdMember.Name};", identity.Arguments[0]);

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
                if (!method.Frames.Any())
                    method.Frames.ThrowNotImplementedException();
        }

        private void buildLoaderCommands(GeneratedType type)
        {
            var load = type.MethodFor("BuildLoadCommand");
            var loadByArray = type.MethodFor("BuildLoadManyCommand");


            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                load.Frames.Code(
                    "return new NpgsqlCommand(_loaderSql).With(\"id\", id).With(TenantIdArgument.ArgName, tenant.TenantId);");
                loadByArray.Frames.Code(
                    "return new NpgsqlCommand(_loadArraySql).With(\"ids\", ids).With(TenantIdArgument.ArgName, tenant.TenantId);");
            }
            else
            {
                load.Frames.Code("return new NpgsqlCommand(_loaderSql).With(\"id\", id);");
                loadByArray.Frames.Code("return new NpgsqlCommand(_loadArraySql).With(\"ids\", ids);");
            }
        }

        private void buildStorageOperationMethods(DocumentOperations operations, GeneratedType type)
        {
            buildOperationMethod(type, operations, "Upsert");
            buildOperationMethod(type, operations, "Insert");
            buildOperationMethod(type, operations, "Update");

            if (_mapping.UseOptimisticConcurrency)
            {
                buildOperationMethod(type, operations, "Overwrite");
            }
            else
            {
                type.MethodFor("Overwrite").Frames.ThrowNotSupportedException();
            }

            type.MethodFor("DeleteForDocument").Frames.Code($@"
return new Marten.Generated.{operations.DeleteById.TypeName}(Identity({{0}}));
", new Use(_mapping.DocumentType));

            type.MethodFor("DeleteForId").Frames.Code($@"
return new Marten.Generated.{operations.DeleteById.TypeName}({{0}});
", new Use(_mapping.IdType));

            type.MethodFor("DeleteForWhere").Frames.Code($@"
return new Marten.Generated.{operations.DeleteByWhere.TypeName}({{0}});
", Use.Type<IWhereFragment>());
        }

        // TODO -- just inject the type alias and simplify the operation classes
        private void buildOperationMethod(GeneratedType type, DocumentOperations operations, string methodName)
        {
            var operationType = (GeneratedType)typeof(DocumentOperations).GetProperty(methodName).GetValue(operations);
            var method = type.MethodFor(methodName);

            if (_mapping.IsHierarchy())
            {
                method.Frames
                    .Code($@"
return new Marten.Generated.{operationType.TypeName}
(
    {{0}}, Identity({{0}}),
    {{1}}.Versions.ForType<{_mapping.DocumentType.FullNameInCode()},
    {_mapping.IdType.FullNameInCode()}>(),
    {{2}}
);"
                        , new Use(_mapping.DocumentType), Use.Type<IMartenSession>(), Use.Type<DocumentMapping>());
            }
            else
            {
                method.Frames
                    .Code($@"
return new Marten.Generated.{operationType.TypeName}
(
    {{0}}, Identity({{0}}),
    {{1}}.Versions.ForType<{_mapping.DocumentType.FullNameInCode()},
    {_mapping.IdType.FullNameInCode()}>()
);"
                        , new Use(_mapping.DocumentType), Use.Type<IMartenSession>());
            }
        }

    }
}
