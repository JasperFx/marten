using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Internal.Storage;
using Marten.Util;

namespace Marten.Events
{
    internal class ProjectionCodeFile: ICodeFile
    {
        private readonly IGeneratedProjection _projection;
        private readonly StoreOptions _options;

        public ProjectionCodeFile(IGeneratedProjection projection, StoreOptions options)
        {
            _projection = projection;
            _options = options;
        }

        public void AssembleTypes(GeneratedAssembly assembly)
        {
            _projection.AssembleTypes(assembly, _options);
        }

        public Task<bool> AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            var attached = _projection.TryAttachTypes(assembly, _options);
            return Task.FromResult(attached);
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            return _projection.TryAttachTypes(assembly, _options);
        }

        public string FileName => _projection.GetType().ToSuffixedTypeName("RuntimeSupport");
    }

    public partial class EventGraph : ICodeFileCollection, ICodeFile
    {
        private Type _storageType;

        public GenerationRules Rules => Options.CreateGenerationRules();

        IReadOnlyList<ICodeFile> ICodeFileCollection.BuildFiles()
        {
            var list = new List<ICodeFile> { this };

            var projections = Options.Projections.All.OfType<IGeneratedProjection>()
                .Select(x => new ProjectionCodeFile(x, Options));
            list.AddRange(projections);

            return list;
        }

        internal DocumentProvider<IEvent> Provider { get; private set; }

        string ICodeFileCollection.ChildNamespace { get; } = "EventStore";

        void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
        {
            EventDocumentStorageGenerator.AssembleTypes(Options, assembly);
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            var storageType = assembly.FindPreGeneratedType(@containingNamespace,
                EventDocumentStorageGenerator.EventDocumentStorageTypeName);

            if (storageType == null)
            {
                Provider = null;
            }
            else
            {
                var storage = (EventDocumentStorage)Activator.CreateInstance(storageType, Options);
                Provider = new DocumentProvider<IEvent>(null, storage, storage, storage, storage);
            }

            return Provider != null;
        }

        Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services, string containingNamespace)
        {
            var found = AttachTypesSynchronously(rules, assembly, services, containingNamespace);
            return Task.FromResult(found);
        }

        string ICodeFile.FileName => "EventStorage";

    }
}
