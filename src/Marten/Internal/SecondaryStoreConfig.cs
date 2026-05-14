using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.RuntimeCompiler;
using Marten.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal;

[UnconditionalSuppressMessage("Trimming", "IL2077",
    Justification = "Class-level: DAM-annotated field/property assigned from a reflective lookup whose source type is preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class SecondaryDocumentStores: ICodeFileCollection
{
    private readonly List<IStoreConfig> _files = new();

    public IServiceProvider Services { get; set; }

    public IReadOnlyList<ICodeFile> BuildFiles()
    {
        return _files;
    }

    public string ChildNamespace { get; } = "Stores";


    public GenerationRules Rules
    {
        get
        {
            var parentRules = _files.First().BuildStoreOptions(Services).CreateGenerationRules();
            var rules = new GenerationRules();

            rules.GeneratedNamespace = "Marten.Generated";
            rules.GeneratedCodeOutputPath = parentRules.GeneratedCodeOutputPath.ParentDirectory();

            return rules;
        }
    }

    public void Add(IStoreConfig config)
    {
        _files.Add(config);
        config.Parent = this;
    }
}

internal interface IStoreConfig: ICodeFile
{
    SecondaryDocumentStores Parent { get; set; }
    StoreOptions BuildStoreOptions(IServiceProvider provider);
}

public interface IConfigureMarten<T>: IConfigureMarten where T : IDocumentStore
{
}


[UnconditionalSuppressMessage("Trimming", "IL2077",
    Justification = "Class-level: DAM-annotated field/property assigned from a reflective lookup whose source type is preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class SecondaryStoreConfig<T>: IStoreConfig where T : IDocumentStore
{
    private readonly Func<IServiceProvider, StoreOptions> _configuration;
    private Type? _storeType;

    public SecondaryStoreConfig(Func<IServiceProvider, StoreOptions> configuration)
    {
        _configuration = configuration;
        FileName = typeof(T).ToSuffixedTypeName("Implementation");
    }

    public void AssembleTypes(GeneratedAssembly assembly)
    {
        var type = assembly.AddType(FileName, typeof(DocumentStore));
        type.Implements<T>();
    }

    public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        return Task.FromResult(AttachTypesSynchronously(rules, assembly, services, containingNamespace));
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        _storeType = assembly.FindPreGeneratedType(containingNamespace, FileName);
        return _storeType != null;
    }

    public string FileName { get; }

    public SecondaryDocumentStores Parent { get; set; }

    public StoreOptions BuildStoreOptions(IServiceProvider provider)
    {
        var options = _configuration(provider);
        options.StoreName = typeof(T).Name;

        var configures = provider.GetServices<IConfigureMarten<T>>();
        foreach (var configure in configures) configure.Configure(provider, options);

        var globals = provider.GetServices<IConfigureMarten>().OfType<IGlobalConfigureMarten>();
        foreach (var configureMarten in globals)
        {
            configureMarten.Configure(provider, options);
        }

        options.ReadJasperFxOptions(provider.GetService<JasperFxOptions>());
        options.StoreName = typeof(T).Name;
        options.ReadJasperFxOptions(provider.GetService<JasperFxOptions>());
        options.Projections.AttachServiceProvider(provider);
        options.Services = provider;

        return options;
    }

    public T Build(IServiceProvider provider)
    {
        var options = BuildStoreOptions(provider);
        var rules = options.CreateGenerationRules();

        rules.GeneratedNamespace = SchemaConstants.MartenGeneratedNamespace;
        // CreateGenerationRules() appends StoreName to the output path, but
        // SecondaryDocumentStores.Rules (used by codegen write) strips it via
        // ParentDirectory(). Align the paths to avoid writing duplicate files
        // with the same namespace and class name to different directories (#4185)
        rules.GeneratedCodeOutputPath = rules.GeneratedCodeOutputPath.ParentDirectory();
        // 9.0 (#4309): route through the AllowRuntimeCodeGeneration gate so
        // ancillary stores honor the AOT-friendly opt-out as well.
        Marten.Internal.CodeGeneration.StaticOnlyCodeFileLoader.Initialize(
            this, rules, Parent, provider, options.AllowRuntimeCodeGeneration);

        var store = (T)Activator.CreateInstance(_storeType!, options)!;
        store.As<DocumentStore>().Subject = new Uri("marten://" + typeof(T).Name.ToLowerInvariant());

        return store;
    }
}
