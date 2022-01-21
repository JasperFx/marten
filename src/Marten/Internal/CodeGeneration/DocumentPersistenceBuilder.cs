using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Schema.BulkLoading;
using Npgsql;
using CommandExtensions = Weasel.Postgresql.CommandExtensions;

namespace Marten.Internal.CodeGeneration
{
    public class DocumentPersistenceBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly StoreOptions _options;

        public DocumentPersistenceBuilder(DocumentMapping mapping, StoreOptions options)
        {
            _mapping = mapping;
            _options = options;

            ProviderName = $"{mapping.DocumentType.Name.Sanitize()}Provider";
        }

        public string ProviderName { get; }

        public void AssemblyTypes(GeneratedAssembly assembly)
        {
            var operations = new DocumentOperations(assembly, _mapping, _options);

            assembly.Namespaces.Add(typeof(CommandExtensions).Namespace);
            assembly.Namespaces.Add(typeof(TenantIdArgument).Namespace);
            assembly.Namespaces.Add(typeof(NpgsqlCommand).Namespace);
            assembly.Namespaces.Add(typeof(Weasel.Core.CommandExtensions).Namespace);


            new DocumentStorageBuilder(_mapping, StorageStyle.QueryOnly, x => x.QueryOnlySelector)
                .Build(assembly, operations);

            new DocumentStorageBuilder(_mapping, StorageStyle.Lightweight, x => x.LightweightSelector)
                .Build(assembly, operations);

            new DocumentStorageBuilder(_mapping, StorageStyle.IdentityMap, x => x.IdentityMapSelector)
                .Build(assembly, operations);

            new DocumentStorageBuilder(_mapping, StorageStyle.DirtyTracking, x => x.DirtyCheckingSelector)
                .Build(assembly, operations);

            new BulkLoaderBuilder(_mapping).BuildType(assembly);
        }

        public static DocumentProvider<T> FromPreBuiltTypes<T>(Assembly assembly, DocumentMapping mapping)
        {
            var providerType = assembly.ExportedTypes.FirstOrDefault(x =>
                x.Name == new DocumentPersistenceBuilder(mapping, mapping.StoreOptions).ProviderName);

            return Activator.CreateInstance(providerType, mapping) as DocumentProvider<T>;
        }

        public DocumentProvider<T> Generate<T>()
        {
            var assembly = new GeneratedAssembly(new GenerationRules(SchemaConstants.MartenGeneratedNamespace));

            var operations = new DocumentOperations(assembly, _mapping, _options);

            assembly.Namespaces.Add(typeof(CommandExtensions).Namespace);
            assembly.Namespaces.Add(typeof(Weasel.Core.CommandExtensions).Namespace);
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

            var types = new[]
            {
                typeof(IDocumentStorage<>),
                typeof(T),
            };

            foreach (var referencedAssembly in WalkReferencedAssemblies.ForTypes(types))
            {
                compiler.ReferenceAssembly(referencedAssembly);
            }

            var providerType = assembly.AddType(ProviderName,
                typeof(DocumentProvider<>).MakeGenericType(_mapping.DocumentType));
            providerType.AllInjectedFields.Clear();

            providerType.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));

            var bulkWriterArgType = typeof(IBulkLoader<>).MakeGenericType(_mapping.DocumentType);
            var bulkWriterArgs = $"new {queryOnly.TypeName}(mapping)";
            if (bulkWriterType.AllInjectedFields.Count == 2)
            {
                bulkWriterArgs += ", mapping";
            }

            var bulkWriterCode = $"new {bulkWriterType.TypeName}({bulkWriterArgs})";
            providerType.BaseConstructorArguments[0] = new Variable(bulkWriterArgType, bulkWriterCode);


            providerType.BaseConstructorArguments[1] = new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), queryOnly);
            providerType.BaseConstructorArguments[2] = new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), lightweight);
            providerType.BaseConstructorArguments[3] = new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), identityMap);
            providerType.BaseConstructorArguments[4] = new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), dirtyTracking);


            try
            {
                compiler.Compile(assembly);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("is inaccessible due to its protection level"))
                {
                    throw new InvalidOperationException($"Requested document type '{_mapping.DocumentType.FullNameInCode()}' must be either scoped as 'public' or the assembly holding it must use the {nameof(InternalsVisibleToAttribute)} pointing to 'Marten.Generated'", e);
                }

                throw;
            }

            return (DocumentProvider<T>)Activator.CreateInstance(providerType.CompiledType, _mapping);
        }
    }
}
