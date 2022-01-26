using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal.Sessions;

namespace Marten.Internal.CompiledQueries
{
    internal class CompiledQueryCodeFile: ICodeFile
    {
        private readonly Type _compiledQueryType;
        private readonly StoreOptions _options;
        private readonly string _typeName;
        private Type _sourceType;
        private CompiledQueryPlan _plan;
        private CompiledQuerySourceBuilder _builder;

        public CompiledQueryCodeFile(Type compiledQueryType, StoreOptions options, CompiledQueryPlan plan) : this(compiledQueryType, options)
        {
            _plan = plan;
        }

        public CompiledQueryCodeFile(Type compiledQueryType, StoreOptions options)
        {
            _compiledQueryType = compiledQueryType;
            _options = options;

            _typeName = compiledQueryType.ToSuffixedTypeName("CompiledQuerySource");
        }

        public void AssembleTypes(GeneratedAssembly assembly)
        {
            if (_plan == null)
            {
                using var session = new LightweightSession(_options);
                _plan = QueryCompiler.BuildPlan(session, _compiledQueryType, _options);
            }

            _builder = new CompiledQuerySourceBuilder(_plan, _options);
            _builder.AssembleTypes(assembly);
        }

        public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            _sourceType = assembly.ExportedTypes.FirstOrDefault(x => x.Name == _typeName);
            return Task.FromResult(_sourceType != null);
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            _sourceType = assembly.ExportedTypes.FirstOrDefault(x => x.Name == _typeName);
            return _sourceType != null;
        }

        public ICompiledQuerySource Build(GenerationRules generationRules)
        {
            if (_builder == null)
            {
                AssembleTypes(new GeneratedAssembly(generationRules));
            }

            return _builder.Build(_sourceType);
        }

        public string FileName => _typeName;


    }
}
