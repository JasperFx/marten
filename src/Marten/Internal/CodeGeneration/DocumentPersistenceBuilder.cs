using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Schema.BulkLoading;
using Npgsql;
using CommandExtensions = Weasel.Postgresql.CommandExtensions;

namespace Marten.Internal.CodeGeneration
{
    public class DocumentPersistenceBuilder : ICodeFile
    {
        private readonly DocumentMapping _mapping;
        private readonly StoreOptions _options;
        private Type _providerType;

        public DocumentPersistenceBuilder(DocumentMapping mapping, StoreOptions options)
        {
            _mapping = mapping;
            _options = options;

            ProviderName = mapping.DocumentType.ToSuffixedTypeName("Provider");
        }

        public string ProviderName { get; }

        public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            _providerType = assembly.ExportedTypes.FirstOrDefault(x => x.Name == ProviderName);
            return Task.FromResult(_providerType != null);
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            _providerType = assembly.ExportedTypes.FirstOrDefault(x => x.Name == ProviderName);
            return _providerType != null;
        }

        public string FileName => ProviderName;

        public void AssembleTypes(GeneratedAssembly assembly)
        {
            var operations = new DocumentOperations(assembly, _mapping, _options);

            assembly.UsingNamespaces.Add(typeof(CommandExtensions).Namespace);
            assembly.UsingNamespaces.Add(typeof(TenantIdArgument).Namespace);
            assembly.UsingNamespaces.Add(typeof(NpgsqlCommand).Namespace);
            assembly.UsingNamespaces.Add(typeof(Weasel.Core.CommandExtensions).Namespace);


            var queryOnly = new DocumentStorageBuilder(_mapping, StorageStyle.QueryOnly, x => x.QueryOnlySelector)
                .Build(assembly, operations);

            var lightweight = new DocumentStorageBuilder(_mapping, StorageStyle.Lightweight, x => x.LightweightSelector)
                .Build(assembly, operations);

            var identityMap = new DocumentStorageBuilder(_mapping, StorageStyle.IdentityMap, x => x.IdentityMapSelector)
                .Build(assembly, operations);

            var dirtyTracking = new DocumentStorageBuilder(_mapping, StorageStyle.DirtyTracking, x => x.DirtyCheckingSelector)
                .Build(assembly, operations);

            var bulkWriterType = new BulkLoaderBuilder(_mapping).BuildType(assembly);

            buildProviderType(assembly, queryOnly, bulkWriterType, lightweight, identityMap, dirtyTracking);

            var types = new[]
            {
                typeof(IDocumentStorage<>),
                _mapping.DocumentType,
            };

            assembly.Rules.ReferenceTypes(types);
        }

        public DocumentProvider<T> BuildProvider<T>()
        {
            return (DocumentProvider<T>)Activator.CreateInstance(_providerType, _mapping);
        }

        private GeneratedType buildProviderType(GeneratedAssembly assembly, GeneratedType queryOnly,
            GeneratedType bulkWriterType, GeneratedType lightweight, GeneratedType identityMap, GeneratedType dirtyTracking)
        {
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


            providerType.BaseConstructorArguments[1] =
                new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), queryOnly);
            providerType.BaseConstructorArguments[2] =
                new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), lightweight);
            providerType.BaseConstructorArguments[3] =
                new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), identityMap);
            providerType.BaseConstructorArguments[4] =
                new CreateFromDocumentMapping(_mapping, typeof(IDocumentStorage<>), dirtyTracking);
            return providerType;
        }
    }
}
