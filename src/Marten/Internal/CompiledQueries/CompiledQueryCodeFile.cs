using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;

namespace Marten.Internal.CompiledQueries;

internal class CompiledQueryCodeFile: ICodeFile
{
    private readonly Type _compiledQueryType;
    private readonly DocumentTracking _documentTracking;
    private readonly CompiledQueryPlan _plan;
    private readonly DocumentStore _store;
    private CompiledQuerySourceBuilder _builder;
    private Type _sourceType;

    public CompiledQueryCodeFile(Type compiledQueryType, DocumentStore store, CompiledQueryPlan plan,
        DocumentTracking documentTracking)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _compiledQueryType = compiledQueryType;
        _store = store;

        FileName = documentTracking + compiledQueryType.ToSuffixedTypeName("CompiledQuerySource");
        _documentTracking = documentTracking;
    }

    public void AssembleTypes(GeneratedAssembly assembly)
    {
        _builder = new CompiledQuerySourceBuilder(_plan, _store.Options, _documentTracking);
        _builder.AssembleTypes(assembly);

    }

    public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        _sourceType = assembly.ExportedTypes.FirstOrDefault(x => x.Name == FileName);
        return Task.FromResult(_sourceType != null);
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        _sourceType = assembly.FindPreGeneratedType(containingNamespace, FileName);
        return _sourceType != null;
    }

    public string FileName { get; }

    public ICompiledQuerySource Build(GenerationRules generationRules)
    {
        if (_builder == null)
        {
            AssembleTypes(new GeneratedAssembly(generationRules));
        }

        return _builder.Build(_sourceType);
    }
}
