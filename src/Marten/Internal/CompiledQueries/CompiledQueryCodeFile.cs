using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    internal class CompiledQueryCodeFile: ICodeFile
    {
        private readonly Type _compiledQueryType;
        private readonly DocumentStore _store;
        private readonly string _typeName;
        private Type _sourceType;
        private CompiledQueryPlan _plan;
        private CompiledQuerySourceBuilder _builder;
        private readonly DocumentTracking _documentTracking;

        public CompiledQueryCodeFile(Type compiledQueryType, DocumentStore store, CompiledQueryPlan plan, DocumentTracking documentTracking)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _compiledQueryType = compiledQueryType;
            _store = store;

            _typeName = documentTracking + compiledQueryType.ToSuffixedTypeName("CompiledQuerySource");
            _documentTracking = documentTracking;
        }

        public void AssembleTypes(GeneratedAssembly assembly)
        {
            _builder = new CompiledQuerySourceBuilder(_plan, _store.Options, _documentTracking);
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
            _sourceType = assembly.FindPreGeneratedType(@containingNamespace, _typeName);
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
