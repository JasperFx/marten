using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marten.Internal
{
    internal class SecondaryDocumentStores : ICodeFileCollection
    {
        private readonly List<IStoreConfig> _files = new List<IStoreConfig>();

        public IReadOnlyList<ICodeFile> BuildFiles()
        {
            return _files;
        }

        public IServiceProvider Services { get; set; }
        public string ChildNamespace { get; } = "Stores";

        public void Add(IStoreConfig config)
        {
            _files.Add(config);
            config.Parent = this;
        }


        public GenerationRules Rules
        {
            get
            {
                var rules = _files.FirstOrDefault().BuildStoreOptions(Services).CreateGenerationRules();

                rules.GeneratedCodeOutputPath = rules.GeneratedCodeOutputPath.ParentDirectory().AppendPath("Stores");

                return rules;
            }
        }
    }



    internal interface IStoreConfig : ICodeFile
    {
        SecondaryDocumentStores Parent { get; set; }
        StoreOptions BuildStoreOptions(IServiceProvider provider);
    }

    public interface IConfigureMarten<T> : IConfigureMarten where T : IDocumentStore
    {

    }

    internal class SecondaryStoreConfig<T> : ICodeFile, IStoreConfig where T : IDocumentStore
    {
        private readonly Func<IServiceProvider, StoreOptions> _configuration;
        private readonly string _className;
        private Type _storeType;

        public SecondaryStoreConfig(Func<IServiceProvider, StoreOptions> configuration)
        {
            _configuration = configuration;
            _className = typeof(T).ToSuffixedTypeName("Implementation");
        }

        public SecondaryDocumentStores Parent { get; set; }

        public T Build(IServiceProvider provider)
        {
            var options = BuildStoreOptions(provider);

            var rules = options.CreateGenerationRules();
            rules.GeneratedCodeOutputPath = rules.GeneratedCodeOutputPath.ParentDirectory().AppendPath("Stores");
            rules.GeneratedNamespace = "Marten.Generated.Stores";

            this.InitializeSynchronously(rules, Parent, provider);

            return (T)Activator.CreateInstance(_storeType, options);
        }

        public StoreOptions BuildStoreOptions(IServiceProvider provider)
        {
            var options = _configuration(provider);
            options.StoreName = typeof(T).Name;

            var configures = provider.GetServices<IConfigureMarten<T>>();
            foreach (var configure in configures)
            {
                configure.Configure(provider, options);
            }

            var environment = provider.GetService<IHostEnvironment>();
            if (environment != null)
            {
                options.ReadHostEnvironment(environment);
            }

            return options;
        }

        public void AssembleTypes(GeneratedAssembly assembly)
        {
            var type = assembly.AddType(_className, typeof(DocumentStore));
            type.Implements<T>();
        }

        public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            return Task.FromResult(AttachTypesSynchronously(rules, assembly, services, containingNamespace));
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            _storeType = assembly.FindPreGeneratedType(containingNamespace, _className);
            return _storeType != null;
        }

        public string FileName => _className;
    }
}
