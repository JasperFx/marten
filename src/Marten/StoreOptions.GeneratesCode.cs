using System;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten
{
    public partial class StoreOptions : IGeneratesCode
    {
        public IServiceVariableSource AssemblyTypes(GenerationRules rules, GeneratedAssembly assembly)
        {
            // This is important to ensure that all the possible DocumentMappings exist
            // first
            Storage.BuildAllMappings();
            foreach (var mapping in Storage.AllDocumentMappings)
            {
                var builder = new DocumentPersistenceBuilder(mapping, this);
                builder.AssemblyTypes(assembly);
            }

            return null;
        }

        public Task AttachPreBuiltTypes(GenerationRules rules, Assembly assembly, IServiceProvider services)
        {
            foreach (var mapping in Storage.AllDocumentMappings)
            {
                var builder = typeof(ProviderBuilder<>).CloseAndBuildAs<IProviderBuilder>(mapping.DocumentType);
                builder.BuildAndStore(assembly, mapping, this);
            }

            return Task.CompletedTask;
        }

        private interface IProviderBuilder
        {
            void BuildAndStore(Assembly assembly, DocumentMapping mapping, StoreOptions options);
        }

        private class ProviderBuilder<T> : IProviderBuilder
        {
            public void BuildAndStore(Assembly assembly, DocumentMapping mapping, StoreOptions options)
            {
                var provider = DocumentPersistenceBuilder.FromPreBuiltTypes<T>(assembly, mapping);
                options.Providers.Append(provider);
            }
        }

        public Task AttachGeneratedTypes(GenerationRules rules, IServiceProvider services)
        {
            return Task.CompletedTask;
        }

        public string CodeType => "DocumentStorage";
    }
}
