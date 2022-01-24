using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;

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

        public string FileName => _projection.GetType().FullName.Sanitize();
    }

    public partial class EventGraph : IGeneratesCode, ICodeFile
    {
        IReadOnlyList<ICodeFile> IGeneratesCode.BuildFiles()
        {
            var list = new List<ICodeFile> { this };

            var projections = Options.Projections.All.OfType<IGeneratedProjection>()
                .Select(x => new ProjectionCodeFile(x, Options));
            list.AddRange(projections);

            return list;
        }

        internal DocumentProvider<IEvent> Provider { get; private set; }

        string IGeneratesCode.ChildNamespace { get; } = "EventStore";

        void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
        {
            EventDocumentStorageGenerator.AssembleTypes(Options, assembly);
        }

        public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
            string containingNamespace)
        {
            Provider = EventDocumentStorageGenerator.BuildProviderFromAssembly(assembly, Options);
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
