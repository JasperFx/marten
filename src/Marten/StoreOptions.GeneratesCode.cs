using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using Baseline.ImTools;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Internal.CodeGeneration;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;

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

            if (_compiledQueryTypes.Any())
            {
                using var session = new LightweightSession(this);
                foreach (var compiledQueryType in _compiledQueryTypes)
                {
                    var plan = QueryCompiler.BuildPlan(session, compiledQueryType, this);
                    var builder = new CompiledQuerySourceBuilder(plan, this);
                    builder.AssemblyType(assembly);
                }
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

            if (_compiledQueryTypes.Any())
            {
                using var session = new LightweightSession(this);
                foreach (var compiledQueryType in _compiledQueryTypes)
                {
                    var plan = QueryCompiler.BuildPlan(session, compiledQueryType, this);
                    var builder = new CompiledQuerySourceBuilder(plan, this);
                    var source = builder.CreateFromPreBuiltType(assembly);

                    _querySources = _querySources.AddOrUpdate(compiledQueryType, source);
                }
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
