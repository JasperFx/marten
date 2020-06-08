using System;
using System.Linq;
using LamarCodeGeneration;
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

        public StorageSlot<T> Generate<T>()
        {
            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            var upsertOperationType = new DocumentFunctionOperationBuilder(_mapping, new UpsertFunction(_mapping)).BuildType(assembly);
            var insertOperationType = new DocumentFunctionOperationBuilder(_mapping, new InsertFunction(_mapping)).BuildType(assembly);
            var updateOperationType = new DocumentFunctionOperationBuilder(_mapping, new UpdateFunction(_mapping)).BuildType(assembly);
            if (_mapping.UseOptimisticConcurrency)
            {
                var overwriteOperationType = new DocumentFunctionOperationBuilder(_mapping, new OverwriteFunction(_mapping)).BuildType(assembly);
            }


            var queryOnly = buildQueryOnlyStorage(assembly);
            var lightweight = buildLightweightStorage(assembly);
            var identityMap = buildIdentityMapStorage(assembly);
            var dirtyTracking = buildDirtyTrackingStorage(assembly);

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

        private GeneratedType buildDirtyTrackingStorage(GeneratedAssembly assembly)
        {

            var typeName = $"DirtyTracking{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(DirtyTrackingDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildIdentityMapStorage(GeneratedAssembly assembly)
        {
            var typeName = $"IdentityMap{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(IdentityMapDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildLightweightStorage(GeneratedAssembly assembly)
        {
            var typeName = $"Lightweight{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(LightweightDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildQueryOnlyStorage(GeneratedAssembly assembly)
        {
            var typeName = $"QueryOnly{_mapping.DocumentType.NameInCode()}DocumentStorage";
            var baseType = typeof(QueryOnlyDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            writeNotImplementedStubs(type);

            return type;
        }

        private void writeIdentityMethod(GeneratedType type)
        {
            var identity = type.MethodFor("Identity");
            identity.Frames.Add(new ReturnPropertyFrame(_mapping.DocumentType, _mapping.IdMember));
        }

        private static void writeNotImplementedStubs(GeneratedType type)
        {
            foreach (var method in type.Methods)
            {
                if (!method.Frames.Any())
                {
                    method.Frames.Add(new NotImplementedFrame());
                }
            }
        }
    }
}
