using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.RuntimeCompiler;
using Marten.Schema;
using Marten.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marten.Internal;

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
            var rules = _files.FirstOrDefault().BuildStoreOptions(Services).CreateGenerationRules();

            rules.GeneratedCodeOutputPath = rules.GeneratedCodeOutputPath.ParentDirectory().AppendPath("Stores");

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

internal class SecondaryStoreConfig<T>: ICodeFile, IStoreConfig where T : IDocumentStore
{
    private readonly Func<IServiceProvider, StoreOptions> _configuration;
    private Type _storeType;

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

    public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        return Task.FromResult(AttachTypesSynchronously(rules, assembly, services, containingNamespace));
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
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

        var environment = provider.GetService<IHostEnvironment>();
        if (environment != null)
        {
            options.ReadHostEnvironment(environment);
        }

        options.StoreName = typeof(T).Name;

        return options;
    }

    public T Build(IServiceProvider provider)
    {
        var options = BuildStoreOptions(provider);
        var environment = provider.GetService<IHostEnvironment>();
        if (environment != null)
        {
            options.ReadHostEnvironment(environment);
        }

        var rules = options.CreateGenerationRules();

        rules.GeneratedCodeOutputPath = rules.GeneratedCodeOutputPath.ParentDirectory();
        rules.GeneratedNamespace = SchemaConstants.MartenGeneratedNamespace;
        this.InitializeSynchronously(rules, Parent, provider);

        return (T)Activator.CreateInstance(_storeType, options);
    }
}
