using System;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using Npgsql;

namespace Marten.V4Internals
{
    public class DocumentPersistenceBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly StoreOptions _options;

        public DocumentPersistenceBuilder(DocumentMapping mapping, StoreOptions options)
        {
            _mapping = mapping;
            _options = options;
        }

        public DocumentPersistence<T> Generate<T>()
        {
            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            var operations = new DocumentOperations(assembly, _mapping);

            assembly.Namespaces.Add(typeof(CommandExtensions).Namespace);
            assembly.Namespaces.Add(typeof(TenantIdArgument).Namespace);
            assembly.Namespaces.Add(typeof(NpgsqlCommand).Namespace);


            var queryOnly = new DocumentStorageBuilder(_mapping, StorageStyle.QueryOnly, x => x.QueryOnlySelector)
                .Build(assembly, operations);

            var lightweight = new DocumentStorageBuilder(_mapping, StorageStyle.Lightweight, x => x.LightweightSelector)
                .Build(assembly, operations);

            var identityMap = new DocumentStorageBuilder(_mapping, StorageStyle.IdentityMap, x => x.IdentityMapSelector)
                .Build(assembly, operations);

            var dirtyTracking = new DocumentStorageBuilder(_mapping, StorageStyle.DirtyTracking, x => x.DirtyCheckingSelector)
                .Build(assembly, operations);

            var bulkWriterType = new BulkLoaderBuilder(_mapping).BuildType(assembly);

            var compiler = new AssemblyGenerator();
            compiler.ReferenceAssembly(typeof(IDocumentStorage<>).Assembly);
            compiler.ReferenceAssembly(typeof(T).Assembly);

            compiler.Compile(assembly);

            var slot = new DocumentPersistence<T>
            {
                QueryOnly = (IDocumentStorage<T>)Activator.CreateInstance(queryOnly.CompiledType, _mapping),
                Lightweight = (IDocumentStorage<T>)Activator.CreateInstance(lightweight.CompiledType, _mapping),
                IdentityMap = (IDocumentStorage<T>)Activator.CreateInstance(identityMap.CompiledType, _mapping),
                DirtyTracking = (IDocumentStorage<T>)Activator.CreateInstance(dirtyTracking.CompiledType, _mapping),
                BulkLoader = _mapping.IsHierarchy() ? (IBulkLoader<T>)Activator.CreateInstance(bulkWriterType.CompiledType, _mapping) : (IBulkLoader<T>)Activator.CreateInstance(bulkWriterType.CompiledType)
            };

            return slot;
        }
    }
}
