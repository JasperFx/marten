using System;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Storage;

namespace Marten.V4Internals
{
    /*
 * TODO -- needs to generate:
 * 1. QueryOnly IDocumentStorage
 * 2. Lightweight IDocumentStorage -- tracks version
 * 3. IdentityMap IDocumentStorage
 * 4. DirtyTracing IDocumentStorage
 * 5. IBulkLoader<T> implementation
 * 6. Update operation
 * 7. Upsert operation
 * 8. Insert operation
 * 9. Overwrite operation
 * 10. Delete by id operation
 * 11. Delete by where clause operation
 */


    public enum StorageStyle
    {
        QueryOnly,
        Lightweight,
        IdentityMap,
        DirtyTracking
    }

    public class StorageSlot<T>
    {
        public IDocumentStorage<T> QueryOnly { get; set; }
        public IDocumentStorage<T> Lightweight { get; set; }
        public IDocumentStorage<T> IdentityMap { get; set; }
        public IDocumentStorage<T> DirtyTracking { get; set; }
        public IBulkLoader<T> BulkLoader { get; set; }

        public string SourceCode { get; set; }
    }


    public class DocumentStorageBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly StoreOptions _options;

        public DocumentStorageBuilder(DocumentMapping mapping, StoreOptions options)
        {
            _mapping = mapping;
            _options = options;
        }

        private class Operations
        {
            public GeneratedType Upsert { get; set; }
            public GeneratedType Insert { get; set; }
            public GeneratedType Update { get; set; }
            public GeneratedType Overwrite { get; set; }
        }

        public StorageSlot<T> Generate<T>()
        {
            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            var operations = new Operations
            {
                Upsert = new DocumentFunctionOperationBuilder(_mapping, new UpsertFunction(_mapping), StorageRole.Upsert).BuildType(assembly),
                Insert = new DocumentFunctionOperationBuilder(_mapping, new InsertFunction(_mapping), StorageRole.Insert).BuildType(assembly),
                Update = new DocumentFunctionOperationBuilder(_mapping, new UpdateFunction(_mapping), StorageRole.Update).BuildType(assembly)
            };

            if (_mapping.UseOptimisticConcurrency)
            {
                operations.Overwrite = new DocumentFunctionOperationBuilder(_mapping, new OverwriteFunction(_mapping), StorageRole.Update).BuildType(assembly);
            }


            var queryOnly = buildQueryOnlyStorage(assembly, operations);
            var lightweight = buildLightweightStorage(assembly, operations);
            var identityMap = buildIdentityMapStorage(assembly, operations);
            var dirtyTracking = buildDirtyTrackingStorage(assembly, operations);

            var compiler = new LamarCompiler.AssemblyGenerator();
            compiler.ReferenceAssembly(typeof(IDocumentStorage<>).Assembly);
            compiler.ReferenceAssembly(typeof(T).Assembly);

            compiler.Compile(assembly);

            var slot = new StorageSlot<T>
            {
                QueryOnly = (IDocumentStorage<T>)Activator.CreateInstance(queryOnly.CompiledType, _mapping),
                Lightweight = (IDocumentStorage<T>)Activator.CreateInstance(lightweight.CompiledType, _mapping),
                IdentityMap = (IDocumentStorage<T>)Activator.CreateInstance(identityMap.CompiledType, _mapping),
                DirtyTracking = (IDocumentStorage<T>)Activator.CreateInstance(dirtyTracking.CompiledType, _mapping)
            };

            return slot;
        }

        private GeneratedType buildDirtyTrackingStorage(GeneratedAssembly assembly, Operations operations)
        {

            var typeName = $"DirtyTracking{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(DirtyTrackingDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);
            buildStorageOperationMethods(operations, type);
            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildIdentityMapStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"IdentityMap{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(IdentityMapDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);
            buildStorageOperationMethods(operations, type);
            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildLightweightStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"Lightweight{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(LightweightDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            buildStorageOperationMethods(operations, type);

            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildQueryOnlyStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"QueryOnly{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(QueryOnlyDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            buildStorageOperationMethods(operations, type);

            writeNotImplementedStubs(type);

            return type;
        }

        private void buildStorageOperationMethods(Operations operations, GeneratedType type)
        {
            buildOperationMethod(type, operations, "Upsert");
            buildOperationMethod(type, operations, "Insert");
            buildOperationMethod(type, operations, "Update");
        }

        private void buildOperationMethod(GeneratedType type, Operations operations, string methodName)
        {
            var operationType = (GeneratedType)typeof(Operations).GetProperty(methodName).GetValue(operations);
            var method = type.MethodFor(methodName);

            method.Frames
                .Code($@"return new Marten.Generated.{operationType.TypeName}
    (
        {{0}}, Identity({{0}}),
        {{1}}.Versions.ForType<{_mapping.DocumentType.FullNameInCode()},
        {_mapping.IdType.FullNameInCode()}>()
    );", new Use(_mapping.DocumentType), Use.Type<IMartenSession>());
        }


        private void writeIdentityMethod(GeneratedType type)
        {
            var identity = type.MethodFor("Identity");
            identity.Frames.Code($"return {{0}}.{_mapping.IdMember.Name};", identity.Arguments[0]);
        }

        private static void writeNotImplementedStubs(GeneratedType type)
        {
            foreach (var method in type.Methods)
            {
                if (!method.Frames.Any())
                {
                    method.Frames.ThrowNotImplementedException();
                }
            }
        }
    }
}
